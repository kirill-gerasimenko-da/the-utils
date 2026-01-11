namespace TheUtils.PgTests;

using FluentAssertions;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Pg;

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

        var leftResult = await left.Run(env).RunAsync();
        var rightResult = await right.Run(env).RunAsync();

        leftResult.Should().Be(rightResult);
    }

    [Fact]
    public async Task Monad_RightIdentity_BindPureEqualsOriginal()
    {
        // Right identity: m.Bind(pure) == m
        var env = _fixture.CreatePgEnv();
        var m = pure(42);

        var left = m.Bind(x => pure(x));

        var leftResult = await left.Run(env).RunAsync();
        var rightResult = await m.Run(env).RunAsync();

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

        var leftResult = await left.Run(env).RunAsync();
        var rightResult = await right.Run(env).RunAsync();

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

        var result = await query.Run(env).RunAsync();
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

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be("Test"));
    }

    // ==================== Environment Access ====================

    [Fact]
    public async Task Environment_CanAccessContext()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from e in Pg.env
            select e.Context != null;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Environment_CanAccessDefaultIsolation()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from e in Pg.env
            select e.DefaultIsolation;

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    // ==================== PgEnv Option Defaults ====================

    [Fact]
    public void PgEnv_DefaultRawConnection_UsesContextConnection()
    {
        var context = _fixture.CreateDbContext();
        var env = new PgEnv(context);

        env.RawConnection.IsNone.Should().BeTrue();
        env.Connection.Should().NotBeNull();
    }

    [Fact]
    public void PgEnv_DefaultCommandTimeout_IsNone()
    {
        var context = _fixture.CreateDbContext();
        var env = new PgEnv(context);

        env.CommandTimeout.IsNone.Should().BeTrue();
    }

    [Fact]
    public void PgEnv_ExplicitCommandTimeout_ReturnsValue()
    {
        var context = _fixture.CreateDbContext();
        var timeout = TimeSpan.FromSeconds(30);
        var env = new PgEnv(context, CommandTimeout: timeout);

        env.CommandTimeout.IsSome.Should().BeTrue();
        env.CommandTimeout.IfNone(TimeSpan.Zero).Should().Be(timeout);
    }

    // ==================== Sequencing Operators ====================

    [Fact]
    public async Task Bind_SequencesOperations()
    {
        var env = _fixture.CreatePgEnv();

        // Using Bind with lambda to sequence operations
        var query = add(new User { Name = "BindTest", Email = "bind@test.com" })
            .Bind(_ => saveChanges);

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "bind@test.com");
        user.Should().NotBeNull();
    }

    // ==================== Error Handling ====================

    [Fact]
    public async Task Fail_PropagatesError()
    {
        var env = _fixture.CreatePgEnv();

        var query = fail<int>("Test error");

        var act = async () => await query.Run(env).RunAsync();

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

        var result = await query.Run(env).RunAsync();
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

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Original error*");
    }

    // ==================== Facade Access ====================

    [Fact]
    public async Task Facade_ReturnsDatabaseFacade()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from f in Pg.facade
            select f != null;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    // ==================== LiftIO Operations ====================

    [Fact]
    public async Task LiftIO_WithAsyncFunc_ExecutesOperation()
    {
        var env = _fixture.CreatePgEnv();
        var executed = false;

        var query = Pg.liftIO<int>(async _ =>
        {
            await Task.Delay(1);
            executed = true;
            return 42;
        });

        var result = await query.Run(env).RunAsync();
        result.Should().Be(42);
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task LiftIO_WithSyncFunc_ExecutesOperation()
    {
        var env = _fixture.CreatePgEnv();

        var query = Pg.liftIO(() => 123);

        var result = await query.Run(env).RunAsync();
        result.Should().Be(123);
    }

    [Fact]
    public async Task LiftIO_WithIO_ExecutesOperation()
    {
        var env = _fixture.CreatePgEnv();

        var io = LanguageExt.IO.lift(() => "test");
        var query = Pg.liftIO(io);

        var result = await query.Run(env).RunAsync();
        result.Should().Be("test");
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

        var id = await query.Run(env).RunAsync();

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
            from c in Pg.count(env.Context.Set<User>())
            select c;

        var result = await query.Run(env).RunAsync();
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

        var user = await setupQuery.Run(env).RunAsync();

        // Update
        user.Name = "David";
        var updateQuery =
            from _ in update(user)
            from __ in saveChanges
            select unit;

        await updateQuery.Run(env).RunAsync();

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

        var user = await setupQuery.Run(env).RunAsync();

        // Delete
        var deleteQuery =
            from _ in delete(user)
            from __ in saveChanges
            select unit;

        await deleteQuery.Run(env).RunAsync();

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

        await setup.Run(env).RunAsync();

        // Query
        var query = seq(env.Context.Set<User>());
        var result = await query.Run(env).RunAsync();

        result.Count.Should().Be(3);
    }

    [Fact]
    public async Task Head_ReturnsFirstOrNone()
    {
        var env = _fixture.CreatePgEnv();

        // Empty query
        var emptyResult = await head(env.Context.Set<User>().Where(u => u.Id == -1))
            .Run(env).RunAsync();
        emptyResult.IsNone.Should().BeTrue();

        // With data
        await add(new User { Name = "Frank", Email = "frank@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await head(env.Context.Set<User>())
            .Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
    }

    [Fact]
    public async Task HeadT_ReturnsValueWhenExists()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "HeadT", Email = "headt@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // headT returns OptionT - chain with Map then Run to get Pg<Option<A>>
        var query = headT(env.Context.Set<User>().Where(u => u.Email == "headt@test.com"))
            .Map(u => u.Name)
            .Run()
            .As();

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(name => name.Should().Be("HeadT"));
    }

    [Fact]
    public async Task HeadT_ReturnsNoneWhenNotFound()
    {
        var env = _fixture.CreatePgEnv();

        // headT returns OptionT - when no row found, result is None
        var query = headT(env.Context.Set<User>().Where(u => u.Id == -1))
            .Run()
            .As();

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task Any_ChecksExistence()
    {
        var env = _fixture.CreatePgEnv();

        var beforeAdd = await any(env.Context.Set<User>().Where(u => u.Email == "grace@test.com"))
            .Run(env).RunAsync();
        beforeAdd.Should().BeFalse();

        await add(new User { Name = "Grace", Email = "grace@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var afterAdd = await any(env.Context.Set<User>().Where(u => u.Email == "grace@test.com"))
            .Run(env).RunAsync();
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
          .Run(env).RunAsync();

        var result = await count(env.Context.Set<User>())
            .Run(env).RunAsync();
        result.Should().Be(2);
    }

    [Fact]
    public async Task Single_ReturnsExactlyOne()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "Ivan", Email = "ivan@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await single(env.Context.Set<User>().Where(u => u.Email == "ivan@test.com"))
            .Run(env).RunAsync();
        result.Name.Should().Be("Ivan");
    }

    [Fact]
    public async Task UpdateRange_ModifiesMultipleEntities()
    {
        var env = _fixture.CreatePgEnv();

        // Setup
        var setupQuery =
            from _ in addRange(Seq(
                new User { Name = "UR1", Email = "ur1@test.com", Balance = 10 },
                new User { Name = "UR2", Email = "ur2@test.com", Balance = 20 }
            ))
            from __ in saveChanges
            from result in seq(env.Context.Set<User>().Where(u => u.Email.StartsWith("ur")))
            select result;

        var usersToUpdate = await setupQuery.Run(env).RunAsync();

        // Modify all users
        foreach (var user in usersToUpdate) user.Balance = 100;

        var updateQuery =
            from _ in updateRange(usersToUpdate)
            from __ in saveChanges
            select unit;

        await updateQuery.Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var updated = await verifyContext.Users.Where(u => u.Email.StartsWith("ur")).ToListAsync();
        updated.Should().AllSatisfy(u => u.Balance.Should().Be(100));
    }

    [Fact]
    public async Task DeleteRange_RemovesMultipleEntities()
    {
        var env = _fixture.CreatePgEnv();

        // Setup
        var setupQuery =
            from _ in addRange(Seq(
                new User { Name = "DR1", Email = "dr1@test.com" },
                new User { Name = "DR2", Email = "dr2@test.com" },
                new User { Name = "DR3", Email = "dr3@test.com" }
            ))
            from __ in saveChanges
            from result in seq(env.Context.Set<User>().Where(u => u.Email.StartsWith("dr")))
            select result;

        var usersToDelete = await setupQuery.Run(env).RunAsync();
        usersToDelete.Count.Should().Be(3);

        // Delete all
        var deleteQuery =
            from _ in deleteRange(usersToDelete)
            from __ in saveChanges
            select unit;

        await deleteQuery.Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var remaining = await verifyContext.Users.Where(u => u.Email.StartsWith("dr")).CountAsync();
        remaining.Should().Be(0);
    }

    // ==================== Raw SQL ====================

    [Fact]
    public async Task Execute_RunsRawSql()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "Julia", Email = "julia@test.com", Balance = 50 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var affected = await execute($"UPDATE users SET balance = 200 WHERE email = 'julia@test.com'")
            .Run(env).RunAsync();

        affected.Should().Be(1);

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "julia@test.com");
        user.Balance.Should().Be(200);
    }

    [Fact]
    public async Task ExecuteRaw_WithParams_RunsSql()
    {
        var env = _fixture.CreatePgEnv();

        await add(new User { Name = "RawExec", Email = "rawexec@test.com", Balance = 10 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var affected = await executeRaw("UPDATE users SET balance = {0} WHERE email = {1}", Seq<object>(999m, "rawexec@test.com"))
            .Run(env).RunAsync();

        affected.Should().Be(1);

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "rawexec@test.com");
        user.Balance.Should().Be(999);
    }

    // ==================== FormattableString Query Overloads ====================

    [Fact]
    public async Task Seq_WithFormattableString_ReturnsResults()
    {
        var env = _fixture.CreatePgEnv();

        await addRange(Seq(
            new User { Name = "SeqFS1", Email = "seqfs1@test.com", Balance = 100 },
            new User { Name = "SeqFS2", Email = "seqfs2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Use scalar projection with SqlQuery - it works for primitive/value types
        var minBalance = 50m;
        var result = await seq<string>($"SELECT name FROM users WHERE balance > {minBalance} ORDER BY name")
            .Run(env).RunAsync();

        result.Count.Should().Be(2);
        result[0].Should().Be("SeqFS1");
        result[1].Should().Be("SeqFS2");
    }

    [Fact]
    public async Task Seq_WithRawSqlAndParams_ReturnsResults()
    {
        var env = _fixture.CreatePgEnv();

        await addRange(Seq(
            new User { Name = "SeqRaw1", Email = "seqraw1@test.com" },
            new User { Name = "SeqRaw2", Email = "seqraw2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await seq<User>("SELECT * FROM users WHERE email IN ({0}, {1})", Seq<object>("seqraw1@test.com", "seqraw2@test.com"))
            .Run(env).RunAsync();

        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task Any_WithFormattableString_ChecksExistence()
    {
        var env = _fixture.CreatePgEnv();
        var email = "anyfs@test.com";

        var before = await any<User>($"SELECT * FROM users WHERE email = {email}")
            .Run(env).RunAsync();
        before.Should().BeFalse();

        await add(new User { Name = "AnyFS", Email = email })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var after = await any<User>($"SELECT * FROM users WHERE email = {email}")
            .Run(env).RunAsync();
        after.Should().BeTrue();
    }

    [Fact]
    public async Task Count_WithFormattableString_ReturnsCount()
    {
        var env = _fixture.CreatePgEnv();
        var email1 = "countfs1@test.com";
        var email2 = "countfs2@test.com";
        var email3 = "countfs3@test.com";

        await addRange(Seq(
            new User { Name = "CountFS1", Email = email1 },
            new User { Name = "CountFS2", Email = email2 },
            new User { Name = "CountFS3", Email = email3 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await count<User>($"SELECT * FROM users WHERE email IN ({email1}, {email2}, {email3})")
            .Run(env).RunAsync();

        result.Should().Be(3);
    }

    [Fact]
    public async Task Head_WithFormattableString_ReturnsFirstOrNone()
    {
        var env = _fixture.CreatePgEnv();
        var email = "headfs@test.com";

        // Empty result
        var empty = await head<User>($"SELECT * FROM users WHERE email = {email}")
            .Run(env).RunAsync();
        empty.IsNone.Should().BeTrue();

        // With data
        await add(new User { Name = "HeadFS", Email = email })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await head<User>($"SELECT * FROM users WHERE email = {email}")
            .Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(u => u.Name.Should().Be("HeadFS"));
    }

    [Fact]
    public async Task HeadT_WithFormattableString_ReturnsOptionT()
    {
        var env = _fixture.CreatePgEnv();
        var email = "headtfs@test.com";

        await add(new User { Name = "HeadTFS", Email = email })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = headT<User>($"SELECT * FROM users WHERE email = {email}")
            .Map(u => u.Name)
            .Run()
            .As();

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(name => name.Should().Be("HeadTFS"));
    }

    [Fact]
    public async Task Single_WithFormattableString_ReturnsOne()
    {
        var env = _fixture.CreatePgEnv();
        var email = "singlefs@test.com";

        await add(new User { Name = "SingleFS", Email = email })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await single<User>($"SELECT * FROM users WHERE email = {email}")
            .Run(env).RunAsync();

        result.Name.Should().Be("SingleFS");
    }

    [Fact]
    public async Task Query_WithFormattableString_CreatesQueryable()
    {
        var env = _fixture.CreatePgEnv();

        await addRange(Seq(
            new User { Name = "QueryFS1", Email = "queryfs1@test.com", Balance = 150 },
            new User { Name = "QueryFS2", Email = "queryfs2@test.com", Balance = 250 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Use scalar projection - SqlQuery works with primitives
        // Note: LINQ operators on SqlQuery result may not translate well, so use ORDER BY in SQL
        var minBalance = 100m;
        var result = await (
            from q in query<decimal>($"SELECT balance FROM users WHERE balance > {minBalance} ORDER BY balance DESC")
            from list in seq(q)
            select list
        ).Run(env).RunAsync();

        result.Count.Should().Be(2);
        result[0].Should().Be(250);
        result[1].Should().Be(150);
    }

    [Fact]
    public async Task Query_WithRawSqlAndParams_CreatesQueryable()
    {
        var env = _fixture.CreatePgEnv();
        var email1 = "queryraw1@test.com";
        var email2 = "queryraw2@test.com";

        await addRange(Seq(
            new User { Name = "QueryRaw1", Email = email1 },
            new User { Name = "QueryRaw2", Email = email2 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var result = await (
            from q in query<User>("SELECT * FROM users WHERE email IN ({0}, {1})", Seq<object>(email1, email2))
            from list in seq(q.OrderByDescending(u => u.Email))
            select list
        ).Run(env).RunAsync();

        result.Count.Should().Be(2);
        result[0].Email.Should().Be(email2);
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

        await query.Run(env).RunAsync();

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

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "rollback@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task Transaction_CanCheckCurrentTransaction()
    {
        var env = _fixture.CreatePgEnv();

        // Before transaction - no current transaction
        var beforeTx = await currentTransaction.Run(env).RunAsync();
        beforeTx.IsNone.Should().BeTrue();

        // Inside transaction - has current transaction
        var insideQuery = transact(
            from tx in currentTransaction
            select tx.IsSome
        );
        var insideTx = await insideQuery.Run(env).RunAsync();
        insideTx.Should().BeTrue();

        // After transaction - no current transaction
        var afterTx = await currentTransaction.Run(env).RunAsync();
        afterTx.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task BeginTransaction_StartsNewTransaction()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from tx in beginTransaction()
            from hasTx in currentTransaction.Map(t => t.IsSome)
            from _ in commit
            select hasTx;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BeginTransaction_WithIsolationLevel_StartsTransaction()
    {
        var env = _fixture.CreatePgEnv();
        var isolationLevel = System.Data.IsolationLevel.Serializable;

        // Just verify that a transaction with specified isolation level can be started
        var query =
            from tx in beginTransaction(isolationLevel)
            from hasTx in currentTransaction.Map(t => t.IsSome)
            from _ in commit
            select hasTx;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Commit_CommitsCurrentTransaction()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "ManualCommit", Email = "manualcommit@test.com" })
            from __ in saveChanges
            from ___ in commit
            select unit;

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "manualcommit@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task Rollback_RollsBackCurrentTransaction()
    {
        var env = _fixture.CreatePgEnv();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "ManualRollback", Email = "manualrollback@test.com" })
            from __ in saveChanges
            from ___ in rollback
            select unit;

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "manualrollback@test.com");
        user.Should().BeNull();
    }
}

/// <summary>
/// Tests for Npgsql-specific Pg monad operations.
/// </summary>
[Collection("PostgreSQL")]
public class PgNpgsqlTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgNpgsqlTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Advisory Locks ====================

    [Fact]
    public async Task TryAdvisoryLock_AcquiresLock_ReturnsTrue()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 12345L;

        var query =
            from acquired in tryAdvisoryLock(lockKey)
            from _ in advisoryUnlock(lockKey)
            select acquired;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAdvisoryLock_WhenLocked_ReturnsFalse()
    {
        var env1 = _fixture.CreatePgEnvWithConnection();
        var env2 = _fixture.CreatePgEnvWithConnection();
        var lockKey = 54321L;

        // First connection acquires lock
        var acquired1 = await tryAdvisoryLock(lockKey).Run(env1).RunAsync();
        acquired1.Should().BeTrue();

        // Second connection tries to acquire same lock - should fail
        var acquired2 = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired2.Should().BeFalse();

        // Release lock from first connection
        await advisoryUnlock(lockKey).Run(env1).RunAsync();

        // Now second connection can acquire
        var acquired3 = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired3.Should().BeTrue();

        // Cleanup
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task WithAdvisoryLock_ExecutesWithLock()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 99999L;
        var executed = false;

        var query = withAdvisoryLock(lockKey,
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
        var env1 = _fixture.CreatePgEnvWithConnection();
        var env2 = _fixture.CreatePgEnvWithConnection();
        var lockKey = 88888L;

        // First connection acquires lock and fails
        var query = withAdvisoryLock(lockKey,
            fail<int>("Intentional failure")
        );

        var act = async () => await query.Run(env1).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Second connection should be able to acquire the lock (it was released)
        var acquired = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired.Should().BeTrue();

        // Cleanup
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    // ==================== Raw Queries ====================

    [Fact]
    public async Task RawQuery_WithMapper_ReturnsResults()
    {
        var env = _fixture.CreatePgEnvWithConnection();

        await addRange(Seq(
            new User { Name = "RQ1", Email = "rq1@test.com", Balance = 100 },
            new User { Name = "RQ2", Email = "rq2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = rawQuery<(string Name, decimal Balance)>(
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
        var env = _fixture.CreatePgEnvWithConnection();

        await add(new User { Name = "RQP", Email = "rqp@test.com", Balance = 500 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = rawQuery<string>(
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
        var env = _fixture.CreatePgEnvWithConnection();

        await addRange(Seq(
            new User { Name = "RS1", Email = "rs1@test.com" },
            new User { Name = "RS2", Email = "rs2@test.com" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var query = rawScalar<long>("SELECT COUNT(*) FROM users WHERE email LIKE 'rs%'");

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(c => c.Should().Be(2));
    }

    [Fact]
    public async Task RawScalar_ReturnsNone_WhenNull()
    {
        var env = _fixture.CreatePgEnvWithConnection();

        var query = rawScalar<string>("SELECT NULL::text");

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    // ==================== COPY Protocol ====================

    [Fact]
    public async Task BinaryImport_BulkInsertsData()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var now = DateTime.UtcNow;

        var users = Seq(
            (Name: "BI1", Email: "bi1@test.com", Balance: 10m, CreatedAt: now, IsActive: true),
            (Name: "BI2", Email: "bi2@test.com", Balance: 20m, CreatedAt: now, IsActive: true),
            (Name: "BI3", Email: "bi3@test.com", Balance: 30m, CreatedAt: now, IsActive: true)
        );

        var query = binaryImport(
            "users (name, email, balance, created_at, is_active)",
            users,
            (writer, user) =>
            {
                // Note: StartRow is called internally by binaryImport
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

    // ==================== LISTEN/NOTIFY ====================

    [Fact]
    public async Task Listen_SubscribesToChannel()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var channel = "test_listen";

        // Just verify listen/unlisten don't throw
        await listen(channel).Run(env).RunAsync();
        await unlisten(channel).Run(env).RunAsync();
    }

    [Fact]
    public async Task Notify_SendsNotification()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var channel = "test_notify";
        var payload = "test_payload";

        // Just verify notify doesn't throw
        await notify(channel, payload).Run(env).RunAsync();
    }
}

/// <summary>
/// Tests for JSONB operations.
/// </summary>
[Collection("PostgreSQL")]
public class PgJsonbTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgJsonbTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task JsonbPath_ReturnsValue()
    {
        var env = _fixture.CreatePgEnvWithConnection();

        // Insert a document with JSONB metadata
        await add(new Document
            {
                Title = "Test Doc",
                Metadata = """{"author": "Alice", "tags": ["test", "sample"]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = jsonbPath<string>("documents", "metadata", "$.author");

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("Alice"));
    }

    [Fact]
    public async Task JsonbPath_ReturnsNone_WhenNotFound()
    {
        var env = _fixture.CreatePgEnvWithConnection();

        await add(new Document
            {
                Title = "Empty Doc",
                Metadata = """{"title": "test"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var query = jsonbPath<string>("documents", "metadata", "$.nonexistent");

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_WithVars_ReturnsValue()
    {
        var env = _fixture.CreatePgEnvWithConnection();

        await add(new Document
            {
                Title = "Vars Doc",
                Metadata = """{"items": [{"id": 1, "name": "first"}, {"id": 2, "name": "second"}]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Query with variable - find item by id
        var query = jsonbPath<string>(
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
