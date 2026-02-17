namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Performance tests for large batches and complex operations.
/// </summary>
[Collection("PostgreSQL")]
public class DbPerformanceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbPerformanceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Large Batch AddRange ====================

    [Fact]
    public async Task AddRange_LargeBatch_CompletesEfficiently()
    {
        var env = _fixture.CreateDbRT();
        var batchSize = 1000;

        var users = toSeq(Enumerable.Range(1, batchSize)
            .Select(i => new User
            {
                Name = $"BatchUser{i}",
                Email = $"batch{i}@test.com",
                Balance = i * 1.5m,
                IsActive = i % 2 == 0
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Verify all inserted
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Set<User>().CountAsync();
        count.Should().Be(batchSize);
    }

    [Fact]
    public async Task AddRange_LargeBatch_VerifiesDataIntegrity()
    {
        var env = _fixture.CreateDbRT();
        var batchSize = 500;

        var users = toSeq(Enumerable.Range(1, batchSize)
            .Select(i => new User
            {
                Name = $"IntegrityUser{i}",
                Email = $"integrity{i}@test.com",
                Balance = i * 10m
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Verify sum of balances
        await using var verifyContext = _fixture.CreateDbContext();
        var totalBalance = await verifyContext.Set<User>()
            .Where(u => u.Email.StartsWith("integrity"))
            .SumAsync(u => u.Balance);

        // Sum of 1 to 500 * 10 = (500 * 501 / 2) * 10 = 1252500
        totalBalance.Should().Be(1252500m);
    }

    // ==================== Large Query Results ====================

    [Fact]
    public async Task Seq_LargeResultSet_ReturnsAllResults()
    {
        var env = _fixture.CreateDbRT();
        var recordCount = 500;

        // Insert many records
        var users = toSeq(Enumerable.Range(1, recordCount)
            .Select(i => new User
            {
                Name = $"QueryUser{i}",
                Email = $"query{i}@test.com"
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Query all
        var results = await seq(env.Context.Set<User>().Where(u => u.Email.StartsWith("query")))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(recordCount);
    }

    [Fact]
    public async Task Seq_LargeResultSet_OrderedCorrectly()
    {
        var env = _fixture.CreateDbRT();
        var recordCount = 200;

        var users = toSeq(Enumerable.Range(1, recordCount)
            .Select(i => new User
            {
                Name = $"OrderedUser{i:D4}",
                Email = $"ordered{i}@test.com",
                Balance = recordCount - i // Reverse balance
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var results = await seq(env.Context.Set<User>()
            .Where(u => u.Email.StartsWith("ordered"))
            .OrderBy(u => u.Balance))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(recordCount);
        // First should have balance 0 (recordCount - recordCount)
        results[0].Balance.Should().Be(0m);
        // Last should have balance recordCount - 1
        results[recordCount - 1].Balance.Should().Be(recordCount - 1);
    }

    // ==================== Transaction with Many Operations ====================

    [Fact]
    public async Task Transaction_ManyOperations_CommitsSuccessfully()
    {
        var env = _fixture.CreateDbRT();
        var operationCount = 100;

        var query = transact(
            from _ in Enumerable.Range(1, operationCount)
                .Select(i => add(new User
                {
                    Name = $"TxUser{i}",
                    Email = $"tx{i}@test.com"
                }))
                .Aggregate(
                    pure(unit),
                    (acc, next) => acc.Bind(_ => next.Map(_ => unit))
                )
            from __ in saveChanges
            select unit
        );

        await query.RunIO(env).RunAsync();

        // Verify all committed
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Set<User>()
            .CountAsync(u => u.Email.StartsWith("tx") && u.Email.EndsWith("@test.com"));
        count.Should().Be(operationCount);
    }

    [Fact]
    public async Task Transaction_ManyOperations_RollsBackOnFailure()
    {
        var env = _fixture.CreateDbRT();

        // Insert some users first to verify rollback
        await addRange(Seq(
            new User { Name = "PreExisting1", Email = "pre1@test.com" },
            new User { Name = "PreExisting2", Email = "pre2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .RunIO(env).RunAsync();

        var env2 = _fixture.CreateDbRT();

        // Try to add many users but fail at the end
        var query = transact(
            from _ in Enumerable.Range(1, 50)
                .Select(i => add(new User
                {
                    Name = $"RollbackUser{i}",
                    Email = $"rollback{i}@test.com"
                }))
                .Aggregate(
                    pure(unit),
                    (acc, next) => acc.Bind(_ => next.Map(_ => unit))
                )
            from __ in saveChanges
            from ___ in fail<Unit>("Intentional failure after inserts")
            select unit
        );

        var act = async () => await query.RunIO(env2).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // None of the RollbackUsers should exist
        await using var verifyContext = _fixture.CreateDbContext();
        var rollbackCount = await verifyContext.Set<User>()
            .CountAsync(u => u.Email.StartsWith("rollback"));
        rollbackCount.Should().Be(0);

        // Pre-existing users should still be there
        var preCount = await verifyContext.Set<User>()
            .CountAsync(u => u.Email.StartsWith("pre"));
        preCount.Should().Be(2);
    }

    // ==================== Aggregation Performance ====================

    [Fact]
    public async Task Count_LargeDataset_ReturnsCorrectCount()
    {
        var env = _fixture.CreateDbRT();
        var recordCount = 750;

        var users = toSeq(Enumerable.Range(1, recordCount)
            .Select(i => new User
            {
                Name = $"CountUser{i}",
                Email = $"count{i}@test.com"
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var countResult = await count(env.Context.Set<User>().Where(u => u.Email.StartsWith("count")))
            .RunIO(env).RunAsync();

        countResult.Should().Be(recordCount);
    }

    [Fact]
    public async Task Any_LargeDataset_ReturnsTrueForExisting()
    {
        var env = _fixture.CreateDbRT();
        var recordCount = 300;

        var users = toSeq(Enumerable.Range(1, recordCount)
            .Select(i => new User
            {
                Name = $"AnyUser{i}",
                Email = $"any{i}@test.com"
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var exists = await any(env.Context.Set<User>().Where(u => u.Email == "any150@test.com"))
            .RunIO(env).RunAsync();

        exists.Should().BeTrue();
    }

    // ==================== Pagination Performance ====================

    [Fact]
    public async Task Seq_Pagination_WorksWithLargeDataset()
    {
        var env = _fixture.CreateDbRT();
        var totalRecords = 500;
        var pageSize = 50;

        var users = toSeq(Enumerable.Range(1, totalRecords)
            .Select(i => new User
            {
                Name = $"PageUser{i:D4}",
                Email = $"page{i}@test.com",
                Balance = i
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Get page 3 (indices 100-149)
        var page3 = await seq(env.Context.Set<User>()
            .Where(u => u.Email.StartsWith("page"))
            .OrderBy(u => u.Balance)
            .Skip(100)
            .Take(pageSize))
            .RunIO(env).RunAsync();

        page3.Count.Should().Be(pageSize);
        page3[0].Balance.Should().Be(101m); // First of page 3
        page3[pageSize - 1].Balance.Should().Be(150m); // Last of page 3
    }

    [Fact]
    public async Task Seq_LastPage_ReturnsRemainingRecords()
    {
        var env = _fixture.CreateDbRT();
        var totalRecords = 123;
        var pageSize = 50;

        var users = toSeq(Enumerable.Range(1, totalRecords)
            .Select(i => new User
            {
                Name = $"LastPageUser{i}",
                Email = $"lastpage{i}@test.com",
                Balance = i
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Get last page (should have 23 records: 123 - 100)
        var lastPage = await seq(env.Context.Set<User>()
            .Where(u => u.Email.StartsWith("lastpage"))
            .OrderBy(u => u.Balance)
            .Skip(100)
            .Take(pageSize))
            .RunIO(env).RunAsync();

        lastPage.Count.Should().Be(23); // Only 23 remaining
    }

    // ==================== Multiple Sequential Operations ====================

    [Fact]
    public async Task MultipleOperations_Sequential_MaintainsConsistency()
    {
        var env = _fixture.CreateDbRT();

        // Perform many sequential add/update/delete operations
        for (int i = 1; i <= 50; i++)
        {
            await add(new User
            {
                Name = $"SeqOpUser{i}",
                Email = $"seqop{i}@test.com",
                Balance = i * 10m
            }).Bind(_ => saveChanges.Map(_ => unit))
              .RunIO(env).RunAsync();
        }

        // Update all balances
        var users = await seq(env.Context.Set<User>().Where(u => u.Email.StartsWith("seqop")))
            .RunIO(env).RunAsync();

        foreach (var user in users)
        {
            user.Balance *= 2;
        }
        await saveChanges.RunIO(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var totalBalance = await verifyContext.Set<User>()
            .Where(u => u.Email.StartsWith("seqop"))
            .SumAsync(u => u.Balance);

        // Sum of 1 to 50 * 10 * 2 = (50 * 51 / 2) * 10 * 2 = 25500
        totalBalance.Should().Be(25500m);
    }

    // ==================== Complex Query Performance ====================

    [Fact]
    public async Task Seq_ComplexQuery_ReturnsCorrectResults()
    {
        var env = _fixture.CreateDbRT();

        // Insert users with varied attributes
        var users = toSeq(Enumerable.Range(1, 200)
            .Select(i => new User
            {
                Name = $"ComplexUser{i}",
                Email = $"complex{i}@test.com",
                Balance = i % 100, // 0-99 repeating
                IsActive = i % 3 == 0 // Every 3rd is active
            }));

        await addRange(users)
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Complex query: active users with balance > 50, ordered by balance desc, take top 10
        var results = await seq(env.Context.Set<User>()
            .Where(u => u.Email.StartsWith("complex"))
            .Where(u => u.IsActive)
            .Where(u => u.Balance > 50)
            .OrderByDescending(u => u.Balance)
            .Take(10))
            .RunIO(env).RunAsync();

        results.Count.Should().BeLessThanOrEqualTo(10);
        results.All(u => u.IsActive).Should().BeTrue();
        results.All(u => u.Balance > 50).Should().BeTrue();

        // Should be in descending order
        var balances = results.Select(u => u.Balance).ToList();
        balances.Should().BeInDescendingOrder();
    }
}
