namespace TheUtils.DbPostgresqlTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for PostgreSQL-specific operations (advisory locks, raw queries).
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbAdvisoryLockTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbAdvisoryLockTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TryAdvisoryLock_AcquiresLock_ReturnsTrue()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var lockKey = 12345L;

        var query =
            from acquired in PostgresDb.tryAdvisoryLock(lockKey)
            from _ in PostgresDb.advisoryUnlock(lockKey)
            select acquired;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAdvisoryLock_WhenLocked_ReturnsFalse()
    {
        var env1 = _fixture.CreateDbEnvWithConnection();
        var env2 = _fixture.CreateDbEnvWithConnection();
        var lockKey = 54321L;

        // First connection acquires lock
        var acquired1 = await PostgresDb.tryAdvisoryLock(lockKey).Run(env1).RunAsync();
        acquired1.Should().BeTrue();

        // Second connection tries to acquire same lock - should fail
        var acquired2 = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired2.Should().BeFalse();

        // Release lock from first connection
        await PostgresDb.advisoryUnlock(lockKey).Run(env1).RunAsync();

        // Now second connection can acquire
        var acquired3 = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired3.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task WithAdvisoryLock_ExecutesWithLock()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var lockKey = 99999L;
        var executed = false;

        var query = PostgresDb.withAdvisoryLock(lockKey,
            liftIO(() =>
            {
                executed = true;
                return 42;
            })
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(42);
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task WithAdvisoryLock_ReleasesOnError()
    {
        var env1 = _fixture.CreateDbEnvWithConnection();
        var env2 = _fixture.CreateDbEnvWithConnection();
        var lockKey = 88888L;

        // First connection acquires lock and fails
        var query = PostgresDb.withAdvisoryLock(lockKey,
            fail<int>("Intentional failure")
        );

        var act = async () => await query.Run(env1).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Second connection should be able to acquire the lock (it was released)
        var acquired = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(lockKey).Run(env2).RunAsync();
    }
}

/// <summary>
/// Tests for PostgreSQL raw queries.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbRawQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbRawQueryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RawQuery_WithMapper_ReturnsResults()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await addRange(Seq(
            new User { Name = "RQ1", Email = "rq1@test.com", Balance = 100 },
            new User { Name = "RQ2", Email = "rq2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = PostgresDb.rawQuery<(string Name, decimal Balance)>(
            "SELECT name, balance FROM users WHERE email LIKE 'rq%' ORDER BY name",
            reader => (reader.GetString(0), reader.GetDecimal(1))
        );

        var result = await query.Run(env).RunAsync();
        result.Count.Should().Be(2);
        result[0].Name.Should().Be("RQ1");
        result[0].Balance.Should().Be(100);
        result[1].Name.Should().Be("RQ2");
        result[1].Balance.Should().Be(200);
    }

    [Fact]
    public async Task RawQuery_WithParams_ReturnsResults()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await add(new User { Name = "RQP", Email = "rqp@test.com", Balance = 500 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.rawQuery<string>(
            "SELECT name FROM users WHERE balance > @minBalance",
            reader => reader.GetString(0),
            new Npgsql.NpgsqlParameter("minBalance", 400m)
        );

        var result = await query.Run(env).RunAsync();
        result.ToList().Should().Contain("RQP");
    }

    [Fact]
    public async Task RawScalar_ReturnsValue()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await addRange(Seq(
            new User { Name = "RS1", Email = "rs1@test.com" },
            new User { Name = "RS2", Email = "rs2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = PostgresDb.rawScalar<long>("SELECT COUNT(*) FROM users WHERE email LIKE 'rs%'");

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(c => c.Should().Be(2));
    }

    [Fact]
    public async Task RawScalar_ReturnsNone_WhenNull()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.rawScalar<string>("SELECT NULL::text");

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }
}

/// <summary>
/// Tests for COPY protocol operations.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbCopyTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbCopyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BinaryImport_BulkInsertsData()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var users = Seq(
            (Name: "BI1", Email: "bi1@test.com", Balance: 10m, CreatedAt: now, IsActive: true),
            (Name: "BI2", Email: "bi2@test.com", Balance: 20m, CreatedAt: now, IsActive: true),
            (Name: "BI3", Email: "bi3@test.com", Balance: 30m, CreatedAt: now, IsActive: true)
        );

        var query = PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            users,
            (writer, user) =>
            {
                writer.Write(user.Name, NpgsqlDbType.Varchar);
                writer.Write(user.Email, NpgsqlDbType.Varchar);
                writer.Write(user.Balance, NpgsqlDbType.Numeric);
                writer.Write(user.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(user.IsActive, NpgsqlDbType.Boolean);
            }
        );

        var imported = await query.Run(env).RunAsync();
        imported.Should().Be(3UL);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("bi"));
        count.Should().Be(3);
    }
}

/// <summary>
/// Tests for LISTEN/NOTIFY operations.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbListenNotifyTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbListenNotifyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Listen_SubscribesToChannel()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var channel = "test_listen";

        // Just verify listen/unlisten don't throw
        await PostgresDb.listen(channel).Run(env).RunAsync();
        await PostgresDb.unlisten(channel).Run(env).RunAsync();
    }

    [Fact]
    public async Task Notify_SendsNotification()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var channel = "test_notify";
        var payload = "test_payload";

        // Just verify notify doesn't throw
        await PostgresDb.notify(channel, payload).Run(env).RunAsync();
    }
}

/// <summary>
/// Tests for JSONB operations.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbJsonbTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbJsonbTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task JsonbPath_ReturnsValue()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Insert a document with JSONB metadata
        await add(new Document
            {
                Title = "Test Doc",
                Metadata = """{"author": "Alice", "tags": ["test", "sample"]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.jsonbPath<string>("documents", "metadata", "$.author");

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("Alice"));
    }

    [Fact]
    public async Task JsonbPath_ReturnsNone_WhenNotFound()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await add(new Document
            {
                Title = "Empty Doc",
                Metadata = """{"title": "test"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.jsonbPath<string>("documents", "metadata", "$.nonexistent");

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_WithVars_ReturnsValue()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await add(new Document
            {
                Title = "Vars Doc",
                Metadata = """{"items": [{"id": 1, "name": "first"}, {"id": 2, "name": "second"}]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Query with variable - find item by id
        var query = PostgresDb.jsonbPath<string>(
            "documents",
            "metadata",
            "$.items[*] ? (@.id == $targetId).name",
            """{"targetId": 2}"""
        );

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("second"));
    }
}

/// <summary>
/// Tests for nested resource patterns - combining transactions and locks.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbNestedResourceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbNestedResourceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TransactionWithAdvisoryLock_BothReleaseOnSuccess()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var lockKey = 111111L;

        var query = transact(
            from _ in PostgresDb.withAdvisoryLock(lockKey,
                from __ in add(new User { Name = "TxLock", Email = "txlock@test.com" })
                from ___ in saveChanges
                select unit
            )
            select unit
        );

        await query.Run(env).RunAsync();

        // Verify transaction committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "txlock@test.com");
        user.Should().NotBeNull();

        // Verify lock was released (another connection can acquire it)
        var env2 = _fixture.CreateDbEnvWithConnection();
        var canAcquire = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await PostgresDb.advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task TransactionWithAdvisoryLock_BothReleaseOnFailure()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var lockKey = 222222L;

        var query = transact(
            from _ in PostgresDb.withAdvisoryLock(lockKey,
                from __ in add(new User { Name = "TxLockFail", Email = "txlockfail@test.com" })
                from ___ in saveChanges
                from ____ in fail<Unit>("Failure inside lock inside transaction")
                select unit
            )
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Transaction should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "txlockfail@test.com");
        user.Should().BeNull();

        // Lock should be released
        var env2 = _fixture.CreateDbEnvWithConnection();
        var canAcquire = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await PostgresDb.advisoryUnlock(lockKey).Run(env2).RunAsync();
    }
}
