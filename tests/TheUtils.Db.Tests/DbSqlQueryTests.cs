namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for SQL query operations in the Db monad.
/// </summary>
[Collection("PostgreSQL")]
public class DbSqlQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbSqlQueryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== ExecuteRaw Tests ====================

    [Fact]
    public async Task ExecuteRaw_InsertStatement_InsertsRow()
    {
        var env = _fixture.CreateDbEnv();

        var affected = await executeRaw(
            "INSERT INTO users (name, email, balance, created_at, is_active) VALUES ('RawInsert', 'rawinsert@test.com', 100.0, NOW(), true)"
        ).Run(env).RunAsync();

        affected.Should().Be(1);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "rawinsert@test.com");
        user.Should().NotBeNull();
        user!.Name.Should().Be("RawInsert");
    }

    [Fact]
    public async Task ExecuteRaw_UpdateStatement_UpdatesRows()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await addRange(Seq(
            new User { Name = "Update1", Email = "update1@test.com", Balance = 50 },
            new User { Name = "Update2", Email = "update2@test.com", Balance = 50 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Update
        var affected = await executeRaw(
            "UPDATE users SET balance = 100 WHERE email LIKE 'update%'"
        ).Run(env).RunAsync();

        affected.Should().Be(2);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var users = await verifyContext.Users.Where(u => u.Email.StartsWith("update")).ToListAsync();
        users.Should().AllSatisfy(u => u.Balance.Should().Be(100));
    }

    [Fact]
    public async Task ExecuteRaw_DeleteStatement_DeletesRows()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await addRange(Seq(
            new User { Name = "Delete1", Email = "delete1@test.com" },
            new User { Name = "Delete2", Email = "delete2@test.com" },
            new User { Name = "Keep", Email = "keep@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Delete
        var affected = await executeRaw(
            "DELETE FROM users WHERE email LIKE 'delete%'"
        ).Run(env).RunAsync();

        affected.Should().Be(2);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var remaining = await verifyContext.Users.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Email.Should().Be("keep@test.com");
    }

    [Fact]
    public async Task ExecuteRaw_WithParameters_PassesCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await add(new User { Name = "ParamTest", Email = "paramtest@test.com", Balance = 0 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Update with parameter
        var newBalance = 999m;
        var affected = await executeRaw(
            "UPDATE users SET balance = {0} WHERE email = {1}",
            Seq<object>(newBalance, "paramtest@test.com")
        ).Run(env).RunAsync();

        affected.Should().Be(1);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "paramtest@test.com");
        user.Balance.Should().Be(999);
    }

    [Fact]
    public async Task ExecuteRaw_NoMatchingRows_ReturnsZero()
    {
        var env = _fixture.CreateDbEnv();

        var affected = await executeRaw(
            "DELETE FROM users WHERE email = 'nonexistent@test.com'"
        ).Run(env).RunAsync();

        affected.Should().Be(0);
    }

    // ==================== Query with SQL Tests ====================

    [Fact]
    public async Task Query_WithRawSql_ReturnsResults()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await addRange(Seq(
            new User { Name = "Query1", Email = "query1@test.com", Balance = 100 },
            new User { Name = "Query2", Email = "query2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Query using raw SQL through EF Core
        var results = await seq(env.Context.Set<User>()
            .FromSqlRaw("SELECT * FROM users WHERE email LIKE 'query%' ORDER BY name"))
            .Run(env).RunAsync();

        results.Count.Should().Be(2);
        results[0].Name.Should().Be("Query1");
        results[1].Name.Should().Be("Query2");
    }

    // ==================== Seq with SQL Tests ====================

    [Fact]
    public async Task Seq_WithRawSqlAndParams_ReturnsFilteredResults()
    {
        var env = _fixture.CreateDbEnv();

        // Use a very specific email prefix for isolation
        var testId = Guid.NewGuid().ToString("N")[..8];
        var email1 = $"seqparam_{testId}_1@test.com";
        var email2 = $"seqparam_{testId}_2@test.com";
        var email3 = $"seqparam_{testId}_3@test.com";

        // Setup
        await addRange(Seq(
            new User { Name = "SeqSql1", Email = email1, Balance = 50 },
            new User { Name = "SeqSql2", Email = email2, Balance = 150 },
            new User { Name = "SeqSql3", Email = email3, Balance = 250 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Query using LINQ on top of raw SQL for proper filtering
        var results = await seq(env.Context.Set<User>()
            .FromSqlRaw("SELECT * FROM users WHERE balance > {0}", 100m)
            .Where(u => u.Email.StartsWith($"seqparam_{testId}")))
            .Run(env).RunAsync();

        results.Count.Should().Be(2);
        results.ToList().Should().OnlyContain(u => u.Balance > 100);
    }

    // ==================== Transaction with Raw SQL ====================

    [Fact]
    public async Task Transaction_WithRawSql_CommitsOnSuccess()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in executeRaw("INSERT INTO users (name, email, balance, created_at, is_active) VALUES ('TxRaw', 'txraw@test.com', 0, NOW(), true)")
            select unit
        );

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "txraw@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task Transaction_WithRawSql_RollsBackOnFailure()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in executeRaw("INSERT INTO users (name, email, balance, created_at, is_active) VALUES ('TxRawFail', 'txrawfail@test.com', 0, NOW(), true)")
            from __ in fail<Unit>("Force rollback")
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "txrawfail@test.com");
        user.Should().BeNull();
    }

    // ==================== Mixed EF Core and Raw SQL ====================

    [Fact]
    public async Task MixedOperations_EfCoreAndRawSql_WorkTogether()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from entry in add(new User { Name = "EFUser", Email = "efuser@test.com", Balance = 100 })
            from _ in saveChanges
            from __ in executeRaw("INSERT INTO users (name, email, balance, created_at, is_active) VALUES ('RawUser', 'rawuser@test.com', 200, NOW(), true)")
            from c in count(env.Context.Set<User>().Where(u => u.Email.EndsWith("user@test.com")))
            select c
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(2);

        // Verify both exist
        await using var verifyContext = _fixture.CreateDbContext();
        var efUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "efuser@test.com");
        var rawUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "rawuser@test.com");

        efUser.Should().NotBeNull();
        rawUser.Should().NotBeNull();
    }

    // ==================== SQL Injection Prevention ====================

    [Fact]
    public async Task ExecuteRaw_WithParameters_PreventsInjection()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await add(new User { Name = "Safe", Email = "safe@test.com", Balance = 100 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Attempt injection via parameter - should be safely escaped
        var maliciousInput = "'; DELETE FROM users; --";
        var affected = await executeRaw(
            "UPDATE users SET name = {0} WHERE email = 'safe@test.com'",
            Seq<object>(maliciousInput)
        ).Run(env).RunAsync();

        // Should just update the name to the literal string, not execute the injection
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "safe@test.com");
        user.Should().NotBeNull();
        user!.Name.Should().Be(maliciousInput); // Stored as literal string
    }

    // ==================== Database-specific SQL ====================

    [Fact]
    public async Task ExecuteRaw_WithDatabaseSpecificSql_Works()
    {
        var env = _fixture.CreateDbEnv();

        // PostgreSQL datetime function
        await add(new User { Name = "DateTest", Email = "datetest@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var affected = await executeRaw(
            "UPDATE users SET created_at = NOW() WHERE email = 'datetest@test.com'"
        ).Run(env).RunAsync();

        affected.Should().Be(1);
    }
}
