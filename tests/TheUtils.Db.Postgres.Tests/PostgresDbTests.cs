namespace TheUtils.DbPostgresTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for Postgres-specific operations (advisory locks, raw queries).
/// </summary>
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var lockKey = 12345L;

        var query =
            from acquired in PostgresDb.tryAdvisoryLock(conn, lockKey)
            from _ in PostgresDb.advisoryUnlock(conn, lockKey)
            select acquired;

        var result = await query.RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAdvisoryLock_WhenLocked_ReturnsFalse()
    {
        var env = _fixture.CreateDbEnv();
        var conn1 = _fixture.CreateConnection();
        var conn2 = _fixture.CreateConnection();
        var lockKey = 54321L;

        // First connection acquires lock
        var acquired1 = await PostgresDb.tryAdvisoryLock(conn1, lockKey).RunAsync();
        acquired1.Should().BeTrue();

        // Second connection tries to acquire same lock - should fail
        var acquired2 = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        acquired2.Should().BeFalse();

        // Release lock from first connection
        await PostgresDb.advisoryUnlock(conn1, lockKey).RunAsync();

        // Now second connection can acquire
        var acquired3 = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        acquired3.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(conn2, lockKey).RunAsync();
    }

    [Fact]
    public async Task WithAdvisoryLock_ExecutesWithLock()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var lockKey = 99999L;
        var executed = false;

        var query = PostgresDb.withAdvisoryLock(conn, lockKey,
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
        var env = _fixture.CreateDbEnv();
        var conn1 = _fixture.CreateConnection();
        var conn2 = _fixture.CreateConnection();
        var lockKey = 88888L;

        // First connection acquires lock and fails
        var query = PostgresDb.withAdvisoryLock(conn1, lockKey,
            fail<int>("Intentional failure")
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Second connection should be able to acquire the lock (it was released)
        var acquired = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        acquired.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(conn2, lockKey).RunAsync();
    }
}

/// <summary>
/// Tests for PostgreSQL raw queries.
/// </summary>
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "RQ1", Email = "rq1@test.com", Balance = 100 },
            new User { Name = "RQ2", Email = "rq2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = PostgresDb.rawQuery<(string Name, decimal Balance)>(
            conn,
            "SELECT name, balance FROM users WHERE email LIKE 'rq%' ORDER BY name",
            reader => (reader.GetString(0), reader.GetDecimal(1))
        );

        var result = await query.RunAsync();
        result.Count.Should().Be(2);
        result[0].Name.Should().Be("RQ1");
        result[0].Balance.Should().Be(100);
        result[1].Name.Should().Be("RQ2");
        result[1].Balance.Should().Be(200);
    }

    [Fact]
    public async Task RawQuery_WithParams_ReturnsResults()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new User { Name = "RQP", Email = "rqp@test.com", Balance = 500 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.rawQuery<string>(
            conn,
            "SELECT name FROM users WHERE balance > @minBalance",
            reader => reader.GetString(0),
            new NpgsqlParameter("minBalance", 400m)
        );

        var result = await query.RunAsync();
        result.ToList().Should().Contain("RQP");
    }

    [Fact]
    public async Task RawScalar_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "RS1", Email = "rs1@test.com" },
            new User { Name = "RS2", Email = "rs2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = PostgresDb.rawScalar<long>(conn, "SELECT COUNT(*) FROM users WHERE email LIKE 'rs%'");

        var result = await query.RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(c => c.Should().Be(2));
    }

    [Fact]
    public async Task RawScalar_ReturnsNone_WhenNull()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.rawScalar<string>(conn, "SELECT NULL::text");

        var result = await query.RunAsync();
        result.IsNone.Should().BeTrue();
    }
}

/// <summary>
/// Tests for COPY protocol operations.
/// </summary>
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var now = DateTime.UtcNow;

        var users = Seq(
            (Name: "BI1", Email: "bi1@test.com", Balance: 10m, CreatedAt: now, IsActive: true),
            (Name: "BI2", Email: "bi2@test.com", Balance: 20m, CreatedAt: now, IsActive: true),
            (Name: "BI3", Email: "bi3@test.com", Balance: 30m, CreatedAt: now, IsActive: true)
        );

        var query = PostgresDb.binaryImport(
            conn,
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

        var imported = await query.RunAsync();
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
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var channel = "test_listen";

        // Just verify listen/unlisten don't throw
        await PostgresDb.listen(conn, channel).RunAsync();
        await PostgresDb.unlisten(conn, channel).RunAsync();
    }

    [Fact]
    public async Task Notify_SendsNotification()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var channel = "test_notify";
        var payload = "test_payload";

        // Just verify notify doesn't throw
        await PostgresDb.notify(conn, channel, payload).RunAsync();
    }
}

/// <summary>
/// Tests for JSONB operations.
/// </summary>
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        // Insert a document with JSONB metadata
        await add(new Document
            {
                Title = "Test Doc",
                Metadata = """{"author": "Alice", "tags": ["test", "sample"]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.author");

        var result = await query.RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("Alice"));
    }

    [Fact]
    public async Task JsonbPath_ReturnsNone_WhenNotFound()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "Empty Doc",
                Metadata = """{"title": "test"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.nonexistent");

        var result = await query.RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_WithVars_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "Vars Doc",
                Metadata = """{"items": [{"id": 1, "name": "first"}, {"id": 2, "name": "second"}]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Query with variable - find item by id
        var query = PostgresDb.jsonbPath<string>(
            conn,
            "documents",
            "metadata",
            "$.items[*] ? (@.id == $targetId).name",
            """{"targetId": 2}"""
        );

        var result = await query.RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("second"));
    }
}

/// <summary>
/// Tests for nested resource patterns - combining transactions and locks.
/// </summary>
[Collection("Postgres")]
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
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var lockKey = 111111L;

        var query = transact(
            from _ in PostgresDb.withAdvisoryLock(conn, lockKey,
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
        var conn2 = _fixture.CreateConnection();
        var canAcquire = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        canAcquire.Should().BeTrue();
        await PostgresDb.advisoryUnlock(conn2, lockKey).RunAsync();
    }

    [Fact]
    public async Task TransactionWithAdvisoryLock_BothReleaseOnFailure()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var lockKey = 222222L;

        var query = transact(
            from _ in PostgresDb.withAdvisoryLock(conn, lockKey,
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
        var conn2 = _fixture.CreateConnection();
        var canAcquire = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        canAcquire.Should().BeTrue();
        await PostgresDb.advisoryUnlock(conn2, lockKey).RunAsync();
    }
}
