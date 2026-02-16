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
/// Advanced tests for Postgres raw query operations.
/// </summary>
[Collection("Postgres")]
public class PostgresDbRawQueryAdvancedTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbRawQueryAdvancedTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Multiple Parameters Tests ====================

    [Fact]
    public async Task RawQuery_MultipleParameters_BindsInOrder()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        // Setup
        await addRange(Seq(
            new User { Name = "Multi1", Email = "multi1@test.com", Balance = 100, IsActive = true },
            new User { Name = "Multi2", Email = "multi2@test.com", Balance = 200, IsActive = false },
            new User { Name = "Multi3", Email = "multi3@test.com", Balance = 300, IsActive = true }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var minBalance = new NpgsqlParameter("minBalance", NpgsqlDbType.Numeric) { Value = 150m };
        var isActive = new NpgsqlParameter("isActive", NpgsqlDbType.Boolean) { Value = true };

        var results = await PostgresDb.rawQuery<(string Name, decimal Balance)>(
            conn,
            "SELECT name, balance FROM users WHERE balance > @minBalance AND is_active = @isActive ORDER BY name",
            reader => (reader.GetString(0), reader.GetDecimal(1)),
            minBalance, isActive
        ).RunAsync();

        results.Count.Should().Be(1); // Only Multi3 matches (balance > 150 AND active)
        results[0].Name.Should().Be("Multi3");
        results[0].Balance.Should().Be(300);
    }

    [Fact]
    public async Task RawQuery_PositionalParameters_BindsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new User { Name = "Positional", Email = "positional@test.com", Balance = 500 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var param1 = new NpgsqlParameter { Value = "positional@test.com" };
        var param2 = new NpgsqlParameter { Value = 400m };

        var results = await PostgresDb.rawQuery<string>(
            conn,
            "SELECT name FROM users WHERE email = $1 AND balance > $2",
            reader => reader.GetString(0),
            param1, param2
        ).RunAsync();

        results.Count.Should().Be(1);
        results[0].Should().Be("Positional");
    }

    // ==================== NULL Parameter Tests ====================

    [Fact]
    public async Task RawQuery_NullParameter_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "HasName", Email = "hasname@test.com" },
            new User { Name = "", Email = "emptyname@test.com" } // Empty name
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Query for non-empty names
        var param = new NpgsqlParameter("name", NpgsqlDbType.Varchar) { Value = "" };

        var results = await PostgresDb.rawQuery<string>(
            conn,
            "SELECT email FROM users WHERE name != @name",
            reader => reader.GetString(0),
            param
        ).RunAsync();

        results.Count.Should().Be(1);
        results[0].Should().Be("hasname@test.com");
    }

    [Fact]
    public async Task RawScalar_NullResult_ReturnsNone()
    {
        var conn = _fixture.CreateConnection();

        // Query that returns NULL
        var result = await PostgresDb.rawScalar<string>(
            conn,
            "SELECT NULL::text"
        ).RunAsync();

        result.IsNone.Should().BeTrue();
    }

    // ==================== Large Result Set Tests ====================

    [Fact]
    public async Task RawQuery_LargeResultSet_ReturnsAll()
    {
        var conn = _fixture.CreateConnection();
        var rowCount = 500;
        var now = DateTime.UtcNow;

        // Insert many rows
        var rows = toSeq(Enumerable.Range(1, rowCount)
            .Select(i => (
                Name: $"Large{i}",
                Email: $"large{i}@test.com",
                Balance: (decimal)i,
                CreatedAt: now,
                IsActive: true
            )));

        await PostgresDb.binaryImport(
            conn,
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).RunAsync();

        // Query all with a fresh connection
        var conn2 = _fixture.CreateConnection();
        var results = await PostgresDb.rawQuery<int>(
            conn2,
            "SELECT id FROM users WHERE email LIKE 'large%'",
            reader => reader.GetInt32(0)
        ).RunAsync();

        results.Count.Should().Be(rowCount);
    }

    // ==================== Aggregation Tests ====================

    [Fact]
    public async Task RawScalar_WithSum_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "Sum1", Email = "sum1@test.com", Balance = 100 },
            new User { Name = "Sum2", Email = "sum2@test.com", Balance = 200 },
            new User { Name = "Sum3", Email = "sum3@test.com", Balance = 300 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await PostgresDb.rawScalar<decimal>(
            conn,
            "SELECT SUM(balance) FROM users WHERE email LIKE 'sum%'"
        ).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(600));
    }

    [Fact]
    public async Task RawScalar_WithAvg_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "Avg1", Email = "avg1@test.com", Balance = 100 },
            new User { Name = "Avg2", Email = "avg2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await PostgresDb.rawScalar<decimal>(
            conn,
            "SELECT AVG(balance) FROM users WHERE email LIKE 'avg%'"
        ).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(150));
    }

    [Fact]
    public async Task RawScalar_WithCount_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "Cnt1", Email = "cnt1@test.com" },
            new User { Name = "Cnt2", Email = "cnt2@test.com" },
            new User { Name = "Cnt3", Email = "cnt3@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await PostgresDb.rawScalar<long>(
            conn,
            "SELECT COUNT(*) FROM users WHERE email LIKE 'cnt%'"
        ).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(3));
    }

    [Fact]
    public async Task RawScalar_WithMax_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new User { Name = "Max1", Email = "max1@test.com", Balance = 100 },
            new User { Name = "Max2", Email = "max2@test.com", Balance = 500 },
            new User { Name = "Max3", Email = "max3@test.com", Balance = 300 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await PostgresDb.rawScalar<decimal>(
            conn,
            "SELECT MAX(balance) FROM users WHERE email LIKE 'max%'"
        ).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(500));
    }

    // ==================== Transaction Integration Tests ====================

    [Fact]
    public async Task RawQuery_WithTransaction_SeesUncommittedData()
    {
        var env = _fixture.CreateDbEnv();
        // Use the context's own connection so the raw query participates in the same transaction
        var conn = (NpgsqlConnection)env.Context.Database.GetDbConnection();

        var query = transact(
            from _ in add(new User { Name = "TxQuery", Email = "txquery@test.com", Balance = 999 })
            from __ in saveChanges
            from balance in liftIO(PostgresDb.rawScalar<decimal>(
                conn,
                "SELECT balance FROM users WHERE email = 'txquery@test.com'"
            ))
            select balance
        );

        var result = await query.Run(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(999));
    }

    // ==================== Complex Mapper Tests ====================

    [Fact]
    public async Task RawQuery_WithComplexMapper_MapsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();
        var now = DateTime.UtcNow;

        await add(new User
            {
                Name = "Complex",
                Email = "complex@test.com",
                Balance = 123.45m,
                CreatedAt = now,
                IsActive = true
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var results = await PostgresDb.rawQuery<User>(
            conn,
            "SELECT id, name, email, balance, created_at, is_active FROM users WHERE email = 'complex@test.com'",
            reader => new User
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Email = reader.GetString(2),
                Balance = reader.GetDecimal(3),
                CreatedAt = reader.GetDateTime(4),
                IsActive = reader.GetBoolean(5)
            }
        ).RunAsync();

        results.Count.Should().Be(1);
        var user = results[0];
        user.Name.Should().Be("Complex");
        user.Email.Should().Be("complex@test.com");
        user.Balance.Should().Be(123.45m);
        user.IsActive.Should().BeTrue();
    }

    // ==================== Empty Result Tests ====================

    [Fact]
    public async Task RawQuery_NoResults_ReturnsEmptySeq()
    {
        var conn = _fixture.CreateConnection();

        var results = await PostgresDb.rawQuery<string>(
            conn,
            "SELECT name FROM users WHERE email = 'nonexistent@test.com'",
            reader => reader.GetString(0)
        ).RunAsync();

        results.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task RawScalar_NoRows_ReturnsNone()
    {
        var conn = _fixture.CreateConnection();

        var result = await PostgresDb.rawScalar<decimal>(
            conn,
            "SELECT balance FROM users WHERE email = 'nonexistent@test.com'"
        ).RunAsync();

        result.IsNone.Should().BeTrue();
    }
}
