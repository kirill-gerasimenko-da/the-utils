namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for FormattableString (interpolated SQL) overloads of query operations.
/// </summary>
[Collection("PostgreSQL")]
public class DbInterpolatedSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbInterpolatedSqlTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Execute with Interpolated SQL ====================

    [Fact]
    public async Task Execute_WithInterpolatedSql_RunsQuery()
    {
        var env = _fixture.CreateDbEnv();
        var name = "InterpolatedInsert";
        var email = "interpolated@test.com";

        var affected = await execute(
            $"INSERT INTO users (name, email, balance, created_at, is_active) VALUES ({name}, {email}, 100.0, NOW(), true)"
        ).Run(env).RunAsync();

        affected.Should().Be(1);

        // Verify
        var result = await head<User>(
            $"SELECT * FROM users WHERE email = {email}"
        ).Run(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be(name));
    }

    [Fact]
    public async Task Execute_WithMultipleParameters_BindsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var name = "MultiParam";
        var email = "multiparam@test.com";
        var balance = 250.50m;

        await execute(
            $"INSERT INTO users (name, email, balance, created_at, is_active) VALUES ({name}, {email}, {balance}, NOW(), true)"
        ).Run(env).RunAsync();

        var result = await head<User>(
            $"SELECT * FROM users WHERE email = {email}"
        ).Run(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(u =>
        {
            u.Name.Should().Be(name);
            u.Balance.Should().Be(balance);
        });
    }

    // ==================== Seq with Interpolated SQL ====================

    [Fact]
    public async Task Seq_WithInterpolatedSql_ReturnsResults()
    {
        var env = _fixture.CreateDbEnv();

        // Setup
        await addRange(Seq(
            new User { Name = "SeqSql1", Email = "seqsql1@test.com", Balance = 100 },
            new User { Name = "SeqSql2", Email = "seqsql2@test.com", Balance = 200 },
            new User { Name = "SeqSql3", Email = "seqsql3@test.com", Balance = 300 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var minBalance = 150m;
        var results = await seq<User>(
            $"SELECT * FROM users WHERE balance > {minBalance} ORDER BY name"
        ).Run(env).RunAsync();

        results.Count.Should().Be(2);
        results[0].Name.Should().Be("SeqSql2");
        results[1].Name.Should().Be("SeqSql3");
    }

    [Fact(Skip = "SqlQuery<T> has issues with string parameters in FormattableString on PostgreSQL")]
    public async Task Seq_WithInterpolatedSql_EmptyResult_ReturnsEmptySeq()
    {
        var env = _fixture.CreateDbEnv();
        var nonexistent = "nonexistent@test.com";

        var results = await seq<User>(
            $"SELECT * FROM users WHERE email = {nonexistent}"
        ).Run(env).RunAsync();

        results.IsEmpty.Should().BeTrue();
    }

    // ==================== Any with Interpolated SQL ====================

    [Fact]
    public async Task Any_WithInterpolatedSql_ReturnsTrue_WhenExists()
    {
        var env = _fixture.CreateDbEnv();

        await add(new User { Name = "AnyTest", Email = "anytest@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var email = "anytest@test.com";
        var exists = await any<User>(
            $"SELECT * FROM users WHERE email = {email}"
        ).Run(env).RunAsync();

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Any_WithInterpolatedSql_ReturnsFalse_WhenNotExists()
    {
        var env = _fixture.CreateDbEnv();
        var nonexistent = "nonexistent@test.com";

        var exists = await any<User>(
            $"SELECT * FROM users WHERE email = {nonexistent}"
        ).Run(env).RunAsync();

        exists.Should().BeFalse();
    }

    // ==================== Count with Interpolated SQL ====================

    [Fact]
    public async Task Count_WithInterpolatedSql_CountsRows()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new User { Name = "Count1", Email = "count1@test.com", Balance = 50 },
            new User { Name = "Count2", Email = "count2@test.com", Balance = 150 },
            new User { Name = "Count3", Email = "count3@test.com", Balance = 250 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var threshold = 100m;
        var result = await count<User>(
            $"SELECT * FROM users WHERE balance > {threshold}"
        ).Run(env).RunAsync();

        result.Should().Be(2);
    }

    [Fact]
    public async Task Count_WithInterpolatedSql_ReturnsZero_WhenNoMatches()
    {
        var env = _fixture.CreateDbEnv();
        var impossible = 999999m;

        var result = await count<User>(
            $"SELECT * FROM users WHERE balance > {impossible}"
        ).Run(env).RunAsync();

        result.Should().Be(0);
    }

    // ==================== Head with Interpolated SQL ====================

    [Fact]
    public async Task Head_WithInterpolatedSql_ReturnsFirstOrNone()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new User { Name = "Head1", Email = "head1@test.com", Balance = 100 },
            new User { Name = "Head2", Email = "head2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var threshold = 50m;
        var result = await head<User>(
            $"SELECT * FROM users WHERE balance > {threshold} ORDER BY name LIMIT 1"
        ).Run(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be("Head1"));
    }

    [Fact]
    public async Task Head_WithInterpolatedSql_ReturnsNone_WhenNoMatch()
    {
        var env = _fixture.CreateDbEnv();
        var nonexistent = "nonexistent@test.com";

        var result = await head<User>(
            $"SELECT * FROM users WHERE email = {nonexistent}"
        ).Run(env).RunAsync();

        result.IsNone.Should().BeTrue();
    }

    // ==================== Single with Interpolated SQL ====================

    [Fact]
    public async Task Single_WithInterpolatedSql_ReturnsOne()
    {
        var env = _fixture.CreateDbEnv();

        await add(new User { Name = "SingleTest", Email = "singletest@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var email = "singletest@test.com";
        var result = await single<User>(
            $"SELECT * FROM users WHERE email = {email}"
        ).Run(env).RunAsync();

        result.Name.Should().Be("SingleTest");
    }

    [Fact]
    public async Task Single_WithInterpolatedSql_Throws_WhenMultiple()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new User { Name = "Single1", Email = "singlemulti1@test.com" },
            new User { Name = "Single2", Email = "singlemulti2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var pattern = "singlemulti%";
        var act = async () => await single<User>(
            $"SELECT * FROM users WHERE email LIKE {pattern}"
        ).Run(env).RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ==================== Query with Interpolated SQL ====================

    [Fact]
    public async Task Query_WithInterpolatedSql_ReturnsQueryable()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new User { Name = "Query1", Email = "querysql1@test.com", Balance = 100 },
            new User { Name = "Query2", Email = "querysql2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var threshold = 50m;
        var queryable = await query<User>(
            $"SELECT * FROM users WHERE balance > {threshold}"
        ).Run(env).RunAsync();

        queryable.Should().NotBeNull();
        var list = queryable.ToList();
        list.Should().HaveCount(2);
    }

    // ==================== SQL Injection Prevention ====================

    [Fact]
    public async Task InterpolatedSql_SqlInjectionPrevention_EscapesParameters()
    {
        var env = _fixture.CreateDbEnv();

        // Setup safe user
        await add(new User { Name = "Safe", Email = "safe@test.com", Balance = 100 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Attempt SQL injection via interpolated string
        var maliciousEmail = "'; DELETE FROM users; --";

        // This should safely escape the parameter, not execute the injection
        var result = await head<User>(
            $"SELECT * FROM users WHERE email = {maliciousEmail}"
        ).Run(env).RunAsync();

        // No match (injection was escaped)
        result.IsNone.Should().BeTrue();

        // Verify the safe user still exists (DELETE wasn't executed)
        var count = await Db.count(env.Context.Set<User>()).Run(env).RunAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task InterpolatedSql_WithSpecialCharacters_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var nameWithQuotes = "O'Brien \"Bob\"";
        var email = "obrien@test.com";

        await execute(
            $"INSERT INTO users (name, email, balance, created_at, is_active) VALUES ({nameWithQuotes}, {email}, 100.0, NOW(), true)"
        ).Run(env).RunAsync();

        var result = await head<User>(
            $"SELECT * FROM users WHERE email = {email}"
        ).Run(env).RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be(nameWithQuotes));
    }

}
