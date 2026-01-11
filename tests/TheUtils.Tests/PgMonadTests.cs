namespace TheUtils.Tests;

using FluentAssertions;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.PgOps;

/// <summary>
/// Tests for the Pg monad core functionality.
/// </summary>
[Collection("PostgreSQL")]
public class PgMonadTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgMonadTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Monad Laws ====================

    [Fact]
    public async Task Monad_LeftIdentity_PureBindEqualsFunction()
    {
        // Left identity: pure(a).Bind(f) == f(a)
        var env = _fixture.CreatePgEnv();
        var value = 42;
        Func<int, Pg<int>> f = x => pure(x * 2);

        var left = pure(value).Bind(f);
        var right = f(value);

        var leftResult = await left.RunUnit(env).RunAsync();
        var rightResult = await right.RunUnit(env).RunAsync();

        leftResult.Should().Be(rightResult);
    }

    [Fact]
    public async Task Monad_RightIdentity_BindPureEqualsOriginal()
    {
        // Right identity: m.Bind(pure) == m
        var env = _fixture.CreatePgEnv();
        var m = pure(42);

        var left = m.Bind(x => pure(x));

        var leftResult = await left.RunUnit(env).RunAsync();
        var rightResult = await m.RunUnit(env).RunAsync();

        leftResult.Should().Be(rightResult);
    }

    [Fact]
    public async Task Monad_Associativity_BindOrderDoesNotMatter()
    {
        // Associativity: m.Bind(f).Bind(g) == m.Bind(x => f(x).Bind(g))
        var env = _fixture.CreatePgEnv();
        var m = pure(10);
        Func<int, Pg<int>> f = x => pure(x + 5);
        Func<int, Pg<int>> g = x => pure(x * 2);

        var left = m.Bind(f).Bind(g);
        var right = m.Bind(x => f(x).Bind(g));

        var leftResult = await left.RunUnit(env).RunAsync();
        var rightResult = await right.RunUnit(env).RunAsync();

        leftResult.Should().Be(rightResult);
    }

    // ==================== LINQ Query Syntax ====================

    [Fact]
    public async Task LinqQuery_FromSelectWorks()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from x in pure(10)
            from y in pure(20)
            select x + y;

        var result = await query.RunUnit(env).RunAsync();
        result.Should().Be(30);
    }

    [Fact]
    public async Task LinqQuery_WithDatabaseOperationWorks()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from user in add(new User { Name = "Test", Email = "test@example.com" })
            from _ in saveChanges
            from found in head(env.Context.Set<User>().Where(u => u.Email == "test@example.com"))
            select found;

        var result = await query.RunUnit(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be("Test"));
    }

    // ==================== State Management ====================

    [Fact]
    public async Task State_TracksOperationCount()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from _ in add(new User { Name = "User1", Email = "user1@test.com" })
            from __ in add(new User { Name = "User2", Email = "user2@test.com" })
            from ___ in saveChanges
            from s in state
            select s;

        var (result, finalState) = await query.Run(env).RunAsync();

        // Each add and saveChanges increments operation count
        finalState.OperationCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task State_ModifyWorks()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from _ in modifyState(s => s with { OperationCount = 100 })
            from s in state
            select s.OperationCount;

        var result = await query.RunUnit(env).RunAsync();
        result.Should().Be(100);
    }

    // ==================== Environment Access ====================

    [Fact]
    public async Task Environment_CanAccessContext()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from e in PgOps.env
            select e.Context != null;

        var result = await query.RunUnit(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Environment_CanAccessDefaultIsolation()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from e in PgOps.env
            select e.DefaultIsolation;

        var result = await query.RunUnit(env).RunAsync();
        result.Should().Be(System.Data.IsolationLevel.ReadCommitted);
    }

    // ==================== Error Handling ====================

    [Fact]
    public async Task Fail_PropagatesError()
    {
        var env = _fixture.CreatePgEnv();

        var query = fail<int>("Test error");

        var act = async () => await query.RunUnit(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Test error*");
    }

    [Fact]
    public async Task Catch_HandlesError()
    {
        var env = _fixture.CreatePgEnv();

        var query = Pg.Catch(
            fail<int>("Original error"),
            _ => true,
            _ => pure(42)
        ).As();

        var result = await query.RunUnit(env).RunAsync();
        result.Should().Be(42);
    }

    [Fact]
    public async Task Catch_PassesThroughOnNoMatch()
    {
        var env = _fixture.CreatePgEnv();

        var query = Pg.Catch(
            fail<int>("Original error"),
            err => err.Message.Contains("different"),
            _ => pure(42)
        ).As();

        var act = async () => await query.RunUnit(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Original error*");
    }
}

/// <summary>
/// Tests for Pg monad database operations.
/// </summary>
[Collection("PostgreSQL")]
public class PgDatabaseOperationsTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgDatabaseOperationsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== CRUD Operations ====================

    [Fact]
    public async Task Add_InsertsEntity()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from entry in add(new User { Name = "Alice", Email = "alice@test.com", Balance = 100 })
            from _ in saveChanges
            select entry.Entity.Id;

        var id = await query.RunUnit(env).RunAsync();

        id.Should().BeGreaterThan(0);

        // Verify in database
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.FindAsync(id);
        user.Should().NotBeNull();
        user!.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task AddRange_InsertsMultipleEntities()
    {
        var env = _fixture.CreatePgEnv();
        var users = Seq(
            new User { Name = "Bob", Email = "bob@test.com" },
            new User { Name = "Carol", Email = "carol@test.com" }
        );

        var query =
            from _ in addRange(users)
            from __ in saveChanges
            from c in PgOps.count(env.Context.Set<User>())
            select c;

        var result = await query.RunUnit(env).RunAsync();
        result.Should().Be(2);
    }

    [Fact]
    public async Task Update_ModifiesEntity()
    {
        var env = _fixture.CreatePgEnv();

        // Setup
        var setupQuery =
            from entry in add(new User { Name = "Dave", Email = "dave@test.com" })
            from _ in saveChanges
            select entry.Entity;

        var user = await setupQuery.RunUnit(env).RunAsync();

        // Update
        user.Name = "David";
        var updateQuery =
            from _ in update(user)
            from __ in saveChanges
            select unit;

        await updateQuery.RunUnit(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var updated = await verifyContext.Users.FindAsync(user.Id);
        updated!.Name.Should().Be("David");
    }

    [Fact]
    public async Task Delete_RemovesEntity()
    {
        var env = _fixture.CreatePgEnv();

        // Setup
        var setupQuery =
            from entry in add(new User { Name = "Eve", Email = "eve@test.com" })
            from _ in saveChanges
            select entry.Entity;

        var user = await setupQuery.RunUnit(env).RunAsync();

        // Delete
        var deleteQuery =
            from _ in delete(user)
            from __ in saveChanges
            select unit;

        await deleteQuery.RunUnit(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var deleted = await verifyContext.Users.FindAsync(user.Id);
        deleted.Should().BeNull();
    }

    // ==================== Query Operations ====================

    [Fact]
    public async Task Seq_ReturnsAllRows()
    {
        var env = _fixture.CreatePgEnv();

        // Setup
        var setup =
            from _ in addRange(Seq(
                new User { Name = "User1", Email = "u1@test.com" },
                new User { Name = "User2", Email = "u2@test.com" },
                new User { Name = "User3", Email = "u3@test.com" }
            ))
            from __ in saveChanges
            select unit;

        await setup.RunUnit(env).RunAsync();

        // Query
        var query = seq(env.Context.Set<User>());
        var result = await query.RunUnit(env).RunAsync();

        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task Head_ReturnsFirstOrNone()
    {
        var env = _fixture.CreatePgEnv();

        // Empty query
        var emptyResult = await head(env.Context.Set<User>().Where(u => u.Id == -1))
            .RunUnit(env).RunAsync();
        emptyResult.IsNone.Should().BeTrue();

        // With data
        await add(new User { Name = "Frank", Email = "frank@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunUnit(env).RunAsync();

        var result = await head(env.Context.Set<User>())
            .RunUnit(env).RunAsync();
        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public async Task Any_ChecksExistence()
    {
        var env = _fixture.CreatePgEnv();

        var beforeAdd = await any(env.Context.Set<User>().Where(u => u.Email == "grace@test.com"))
            .RunUnit(env).RunAsync();
        beforeAdd.Should().BeFalse();

        await add(new User { Name = "Grace", Email = "grace@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunUnit(env).RunAsync();

        var afterAdd = await any(env.Context.Set<User>().Where(u => u.Email == "grace@test.com"))
            .RunUnit(env).RunAsync();
        afterAdd.Should().BeTrue();
    }

    [Fact]
    public async Task Count_ReturnsRowCount()
    {
        var env = _fixture.CreatePgEnv();

        await addRange(Seq(
            new User { Name = "H1", Email = "h1@test.com" },
            new User { Name = "H2", Email = "h2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .RunUnit(env).RunAsync();

        var result = await count(env.Context.Set<User>())
            .RunUnit(env).RunAsync();
        result.Should().Be(2);
    }

    [Fact]
    public async Task Single_ReturnsExactlyOne()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "Ivan", Email = "ivan@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunUnit(env).RunAsync();

        var result = await single(env.Context.Set<User>().Where(u => u.Email == "ivan@test.com"))
            .RunUnit(env).RunAsync();
        result.Name.Should().Be("Ivan");
    }

    // ==================== Raw SQL ====================

    [Fact]
    public async Task Execute_RunsRawSql()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "Julia", Email = "julia@test.com", Balance = 50 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunUnit(env).RunAsync();

        var affected = await execute($"UPDATE users SET balance = 200 WHERE email = 'julia@test.com'")
            .RunUnit(env).RunAsync();

        affected.Should().Be(1);

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "julia@test.com");
        user.Balance.Should().Be(200);
    }
}

/// <summary>
/// Tests for Pg monad transaction support.
/// </summary>
[Collection("PostgreSQL")]
public class PgTransactionTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgTransactionTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transaction_CommitsOnSuccess()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from _ in add(new User { Name = "Committed", Email = "committed@test.com" })
            from __ in saveChanges
            select unit
        );

        await query.RunUnit(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "committed@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task Transaction_RollsBackOnError()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from _ in add(new User { Name = "ShouldRollback", Email = "rollback@test.com" })
            from __ in saveChanges
            from ___ in fail<Unit>("Intentional failure")
            select unit
        );

        var act = async () => await query.RunUnit(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "rollback@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task Transaction_TracksStateCorrectly()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from _ in beginTransaction()
            from s1 in state
            from __ in add(new User { Name = "TxUser", Email = "txuser@test.com" })
            from ___ in saveChanges
            from ____ in commit
            from s2 in state
            select (HasTxBefore: s1.HasTransaction, HasTxAfter: s2.HasTransaction);

        var (result, _) = await query.Run(env).RunAsync();

        result.HasTxBefore.Should().BeTrue();
        result.HasTxAfter.Should().BeFalse();
    }
}
