namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for edge cases and boundary conditions in the Db monad.
/// </summary>
[Collection("PostgreSQL")]
public class DbEdgeCaseTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbEdgeCaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Empty Collections ====================

    [Fact]
    public async Task AddRange_EmptySeq_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from _ in addRange(Seq<User>())
            from __ in saveChanges
            select unit;

        // Should not throw
        await query.RunIO(env).RunAsync();
    }

    [Fact]
    public async Task DeleteRange_EmptySeq_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from _ in deleteRange(Seq<User>())
            from __ in saveChanges
            select unit;

        // Should not throw
        await query.RunIO(env).RunAsync();
    }

    [Fact]
    public async Task UpdateRange_EmptySeq_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from _ in updateRange(Seq<User>())
            from __ in saveChanges
            select unit;

        // Should not throw
        await query.RunIO(env).RunAsync();
    }

    // ==================== Empty Query Results ====================

    [Fact]
    public async Task Seq_EmptyTable_ReturnsEmptySeq()
    {
        var env = _fixture.CreateDbRT();

        // Query empty table (after reset)
        var result = await seq(env.Context.Set<User>()).RunIO(env).RunAsync();

        result.ToList().Should().BeEmpty();
        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task Head_EmptyResult_ReturnsNone()
    {
        var env = _fixture.CreateDbRT();

        var result = await head(env.Context.Set<User>().Where(u => u.Id == -999))
            .RunIO(env).RunAsync();

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task Any_EmptyResult_ReturnsFalse()
    {
        var env = _fixture.CreateDbRT();

        var result = await any(env.Context.Set<User>().Where(u => u.Id == -999))
            .RunIO(env).RunAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Count_EmptyResult_ReturnsZero()
    {
        var env = _fixture.CreateDbRT();

        var result = await count(env.Context.Set<User>())
            .RunIO(env).RunAsync();

        result.Should().Be(0);
    }

    // ==================== Single Edge Cases ====================

    [Fact]
    public async Task Single_NoResults_Throws()
    {
        var env = _fixture.CreateDbRT();

        var query = single(env.Context.Set<User>().Where(u => u.Id == -999));

        var act = async () => await query.RunIO(env).RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Single_MultipleResults_Throws()
    {
        var env = _fixture.CreateDbRT();

        // Setup multiple users
        await addRange(Seq(
            new User { Name = "Multiple1", Email = "multiple1@test.com" },
            new User { Name = "Multiple2", Email = "multiple2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .RunIO(env).RunAsync();

        var query = single(env.Context.Set<User>());

        var act = async () => await query.RunIO(env).RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Single_ExactlyOneResult_ReturnsIt()
    {
        var env = _fixture.CreateDbRT();

        await add(new User { Name = "Only", Email = "only@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var result = await single(env.Context.Set<User>())
            .RunIO(env).RunAsync();

        result.Email.Should().Be("only@test.com");
    }

    // ==================== Null and Default Values ====================

    [Fact]
    public async Task Add_EntityWithDefaultValues_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var user = new User
        {
            Name = "", // Empty string
            Email = "defaults@test.com",
            Balance = 0,
            IsActive = false
        };

        var query =
            from _ in add(user)
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.Users.SingleAsync(u => u.Email == "defaults@test.com");
        saved.Name.Should().BeEmpty();
        saved.Balance.Should().Be(0);
        saved.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Query_WithNullComparison_Works()
    {
        var env = _fixture.CreateDbRT();

        // Users with non-null emails
        await add(new User { Name = "NotNull", Email = "notnull@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var result = await seq(env.Context.Set<User>().Where(u => u.Email != null))
            .RunIO(env).RunAsync();

        result.ToList().Should().NotBeEmpty();
    }

    // ==================== Large Values ====================

    [Fact]
    public async Task Add_EntityWithMaxLengthName_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var maxLengthName = new string('A', 100); // Max length for name column

        var query =
            from _ in add(new User { Name = maxLengthName, Email = "maxname@test.com" })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "maxname@test.com");
        user.Name.Should().Be(maxLengthName);
    }

    [Fact]
    public async Task Add_EntityWithDecimalPrecision_PreservesPrecision()
    {
        var env = _fixture.CreateDbRT();

        var preciseBalance = 12345.67m;

        var query =
            from _ in add(new User { Name = "Precision", Email = "precision@test.com", Balance = preciseBalance })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "precision@test.com");
        user.Balance.Should().Be(preciseBalance);
    }

    // ==================== Whitespace and Special Characters ====================

    [Fact]
    public async Task Add_EntityWithWhitespaceInName_PreservesWhitespace()
    {
        var env = _fixture.CreateDbRT();

        var nameWithSpaces = "  Name  With  Spaces  ";

        var query =
            from _ in add(new User { Name = nameWithSpaces, Email = "spaces@test.com" })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "spaces@test.com");
        user.Name.Should().Be(nameWithSpaces);
    }

    [Fact]
    public async Task Add_EntityWithSpecialCharacters_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var specialName = "O'Brien \"Bob\" <test>";

        var query =
            from _ in add(new User { Name = specialName, Email = "special@test.com" })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "special@test.com");
        user.Name.Should().Be(specialName);
    }

    [Fact]
    public async Task Add_EntityWithUnicodeCharacters_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var unicodeName = "日本語 العربية 中文 🎉";

        var query =
            from _ in add(new User { Name = unicodeName, Email = "unicode@test.com" })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "unicode@test.com");
        user.Name.Should().Be(unicodeName);
    }

    // ==================== Query Chaining Edge Cases ====================

    [Fact]
    public async Task LongChain_MultipleOperations_Succeeds()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from _ in add(new User { Name = "Chain1", Email = "chain1@test.com" })
            from __ in add(new User { Name = "Chain2", Email = "chain2@test.com" })
            from ___ in add(new User { Name = "Chain3", Email = "chain3@test.com" })
            from ____ in add(new User { Name = "Chain4", Email = "chain4@test.com" })
            from _____ in add(new User { Name = "Chain5", Email = "chain5@test.com" })
            from ______ in saveChanges
            from c in count(env.Context.Set<User>().Where(u => u.Email.StartsWith("chain")))
            select c;

        var result = await query.RunIO(env).RunAsync();
        result.Should().Be(5);
    }

    [Fact]
    public async Task Map_ChainedMaps_ComposesCorrectly()
    {
        var env = _fixture.CreateDbRT();

        var query = pure(5)
            .Map(x => x * 2)
            .Map(x => x + 3)
            .Map(x => x.ToString())
            .Map(s => $"Result: {s}");

        var result = await query.RunIO(env).RunAsync();
        result.Should().Be("Result: 13"); // (5 * 2) + 3 = 13
    }

    // ==================== Pure Values ====================

    [Fact]
    public async Task Pure_WithNullValue_ReturnsNull()
    {
        var env = _fixture.CreateDbRT();

        var query = pure<string?>(null);

        var result = await query.RunIO(env).RunAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task Pure_WithUnit_ReturnsUnit()
    {
        var env = _fixture.CreateDbRT();

        var query = pure(unit);

        var result = await query.RunIO(env).RunAsync();
        result.Should().Be(unit);
    }

    // ==================== DbSet Access ====================

    [Fact]
    public async Task Set_ReturnsCorrectDbSet()
    {
        var env = _fixture.CreateDbRT();

        var userSet = await set<User>().RunIO(env).RunAsync();

        userSet.Should().NotBeNull();
        userSet.EntityType.ClrType.Should().Be(typeof(User));
    }

    [Fact]
    public async Task Set_CanBeUsedForQueries()
    {
        var env = _fixture.CreateDbRT();

        await add(new User { Name = "SetTest", Email = "settest@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        var query =
            from s in set<User>()
            from users in seq(s.Where(u => u.Email == "settest@test.com"))
            select users;

        var result = await query.RunIO(env).RunAsync();
        result.ToList().Should().HaveCount(1);
    }
}
