namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for advanced LINQ operations in the Db monad.
/// </summary>
[Collection("PostgreSQL")]
public class DbLinqAdvancedTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbLinqAdvancedTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Setup Helper ====================

    private async Task SeedTestData(DbRT env)
    {
        await addRange(Seq(
            new User { Name = "Alice", Email = "alice@test.com", Balance = 100, IsActive = true },
            new User { Name = "Bob", Email = "bob@test.com", Balance = 200, IsActive = true },
            new User { Name = "Charlie", Email = "charlie@test.com", Balance = 150, IsActive = false },
            new User { Name = "David", Email = "david@test.com", Balance = 300, IsActive = true },
            new User { Name = "Eve", Email = "eve@test.com", Balance = 100, IsActive = false },
            new User { Name = "Alice", Email = "alice2@test.com", Balance = 250, IsActive = true } // Duplicate name
        )).Bind(_ => saveChanges.Map(_ => unit))
          .RunIO(env).RunAsync();
    }

    // ==================== OrderBy Tests ====================

    [Fact]
    public async Task Seq_WithOrderBy_ReturnsOrdered()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var results = await seq(env.Context.Set<User>().OrderBy(u => u.Name))
            .RunIO(env).RunAsync();

        var names = results.Map(u => u.Name).ToList();
        names.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Seq_WithOrderByDescending_ReturnsDescending()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var results = await seq(env.Context.Set<User>().OrderByDescending(u => u.Balance))
            .RunIO(env).RunAsync();

        var balances = results.Map(u => u.Balance).ToList();
        balances.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Seq_WithThenBy_MultipleOrdering()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var results = await seq(env.Context.Set<User>()
            .OrderBy(u => u.Name)
            .ThenByDescending(u => u.Balance))
            .RunIO(env).RunAsync();

        // Both Alices should be together, with higher balance first
        var alices = results.Where(u => u.Name == "Alice").ToList();
        alices.Should().HaveCount(2);
        alices[0].Balance.Should().BeGreaterThan(alices[1].Balance);
    }

    // ==================== Pagination (Skip/Take) Tests ====================

    [Fact]
    public async Task Seq_WithSkipTake_ReturnsPaginated()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Get second page (skip first 2, take next 2)
        var results = await seq(env.Context.Set<User>()
            .OrderBy(u => u.Email)
            .Skip(2)
            .Take(2))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(2);
    }

    [Fact]
    public async Task Seq_WithTake_LimitsResults()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var results = await seq(env.Context.Set<User>().Take(3))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(3);
    }

    [Fact]
    public async Task Seq_WithSkip_SkipsResults()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var all = await seq(env.Context.Set<User>().OrderBy(u => u.Id))
            .RunIO(env).RunAsync();

        var skipped = await seq(env.Context.Set<User>().OrderBy(u => u.Id).Skip(2))
            .RunIO(env).RunAsync();

        skipped.Count.Should().Be(all.Count - 2);
    }

    // ==================== Distinct Tests ====================

    [Fact]
    public async Task Seq_WithDistinct_ReturnsUnique()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Get distinct names (note: SQLite handles this via LINQ-to-Objects for Select+Distinct)
        var distinctNames = await seq(env.Context.Set<User>()
            .Select(u => u.Name)
            .Distinct())
            .RunIO(env).RunAsync();

        // Should have 5 unique names (Alice appears twice)
        distinctNames.Count.Should().Be(5);
    }

    [Fact]
    public async Task Seq_WithDistinctBalances_ReturnsUnique()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Get distinct balances
        var distinctBalances = await seq(env.Context.Set<User>()
            .Select(u => u.Balance)
            .Distinct())
            .RunIO(env).RunAsync();

        // 100 appears twice, so we should have 5 unique balances
        distinctBalances.Count.Should().Be(5);
    }

    // ==================== GroupBy Tests ====================

    [Fact]
    public async Task Seq_WithGroupBy_ReturnsGroups()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Group by IsActive and count
        var groups = await seq(env.Context.Set<User>()
            .GroupBy(u => u.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() }))
            .RunIO(env).RunAsync();

        groups.Count.Should().Be(2); // true and false groups
        groups.Sum(g => g.Count).Should().Be(6); // Total users
    }

    [Fact]
    public async Task Seq_WithGroupByAndSum_CalculatesAggregates()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Group by IsActive and sum balances
        var groups = await seq(env.Context.Set<User>()
            .GroupBy(u => u.IsActive)
            .Select(g => new { IsActive = g.Key, TotalBalance = g.Sum(u => u.Balance) }))
            .RunIO(env).RunAsync();

        groups.Count.Should().Be(2);
        var activeGroup = groups.Single(g => g.IsActive);
        var inactiveGroup = groups.Single(g => !g.IsActive);

        // Verify sums are correct
        activeGroup.TotalBalance.Should().Be(100 + 200 + 300 + 250); // Alice, Bob, David, Alice2
        inactiveGroup.TotalBalance.Should().Be(150 + 100); // Charlie, Eve
    }

    // ==================== Select (Projection) Tests ====================

    [Fact]
    public async Task Seq_WithSelect_ProjectsFields()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var projections = await seq(env.Context.Set<User>()
            .Select(u => new { u.Name, u.Email }))
            .RunIO(env).RunAsync();

        projections.Count.Should().Be(6);
        projections.All(p => !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.Email))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Seq_WithSelectComputed_ReturnsComputedValues()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var computed = await seq(env.Context.Set<User>()
            .Select(u => new { u.Name, DoubleBalance = u.Balance * 2 }))
            .RunIO(env).RunAsync();

        computed.Count.Should().Be(6);
        computed.All(c => c.DoubleBalance >= 200).Should().BeTrue(); // Min balance * 2 = 100 * 2 = 200
    }

    // ==================== Complex Where Tests ====================

    [Fact]
    public async Task Seq_WithWhere_ComplexPredicate()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var results = await seq(env.Context.Set<User>()
            .Where(u => u.IsActive && u.Balance > 150))
            .RunIO(env).RunAsync();

        // Bob (200), David (300), Alice2 (250) match
        results.Count.Should().Be(3);
        results.All(u => u.IsActive && u.Balance > 150).Should().BeTrue();
    }

    [Fact]
    public async Task Seq_WithWhere_StringOperations()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Find users whose names start with 'A'
        var results = await seq(env.Context.Set<User>()
            .Where(u => u.Name.StartsWith("A")))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(2); // Both Alices
    }

    [Fact]
    public async Task Seq_WithWhere_ContainsOperation()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var targetEmails = new[] { "alice@test.com", "bob@test.com", "nonexistent@test.com" };

        var results = await seq(env.Context.Set<User>()
            .Where(u => targetEmails.Contains(u.Email)))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(2);
    }

    // ==================== First/Last Tests ====================

    [Fact]
    public async Task Head_WithOrdering_ReturnsFirst()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var result = await head(env.Context.Set<User>()
            .OrderBy(u => u.Balance))
            .RunIO(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Balance.Should().Be(100));
    }

    // ==================== Chained Operations ====================

    [Fact]
    public async Task Seq_ChainedOperations_WorkCorrectly()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        // Complex query chain
        var results = await seq(env.Context.Set<User>()
            .Where(u => u.IsActive)
            .OrderByDescending(u => u.Balance)
            .Take(3)
            .Select(u => new { u.Name, u.Balance }))
            .RunIO(env).RunAsync();

        results.Count.Should().Be(3);
        // Should be David (300), Alice2 (250), Bob (200) in order
        results[0].Balance.Should().Be(300);
        results[1].Balance.Should().Be(250);
        results[2].Balance.Should().Be(200);
    }

    // ==================== Count with Conditions ====================

    [Fact]
    public async Task Count_WithPredicate_CountsMatching()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var activeCount = await count(env.Context.Set<User>().Where(u => u.IsActive))
            .RunIO(env).RunAsync();

        activeCount.Should().Be(4); // Alice, Bob, David, Alice2
    }

    // ==================== Any with Conditions ====================

    [Fact]
    public async Task Any_WithComplexPredicate_ChecksCorrectly()
    {
        var env = _fixture.CreateDbRT();
        await SeedTestData(env);

        var hasRichActive = await any(env.Context.Set<User>()
            .Where(u => u.IsActive && u.Balance > 250))
            .RunIO(env).RunAsync();

        hasRichActive.Should().BeTrue(); // David has 300
    }
}
