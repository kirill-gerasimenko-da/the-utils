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

/// <summary>
/// Tests for transaction robustness - commit/rollback guarantees.
/// </summary>
[Collection("PostgreSQL")]
public class PgTransactionRobustnessTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgTransactionRobustnessTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transaction_RollsBack_WhenErrorBeforeSaveChanges()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from _ in add(new User { Name = "BeforeSave", Email = "beforesave@test.com" })
            from __ in fail<Unit>("Error before saveChanges")
            from ___ in saveChanges
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Entity should not be persisted because we failed before saveChanges
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "beforesave@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task Transaction_RollsBack_WhenDbConstraintViolation()
    {
        var env = _fixture.CreatePgEnv();

        // First, create a user
        await (
            from _ in add(new User { Name = "Original", Email = "unique@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Try to insert duplicate email (assuming email has unique constraint - if not, this tests general DB error)
        var query = transact(
            from _ in add(new User { Name = "Duplicate", Email = "unique@test.com" })
            from __ in saveChanges
            select unit
        );

        // This may or may not throw depending on DB constraints
        // The key point is transaction should rollback on any DB error
        try
        {
            await query.Run(env).RunAsync();
        }
        catch
        {
            // Expected - transaction rolled back
        }

        // Verify only one user with this email exists
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email == "unique@test.com");
        count.Should().Be(1);
    }

    [Fact]
    public async Task Transaction_PreservesExceptionType_AfterRollback()
    {
        var env = _fixture.CreatePgEnv();

        var customMessage = "Custom error message for testing";
        var query = transact(
            from _ in add(new User { Name = "ExType", Email = "extype@test.com" })
            from __ in saveChanges
            from ___ in fail<Unit>(customMessage)
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();

        // Exception should contain the original error message
        await act.Should().ThrowAsync<Exception>()
            .WithMessage($"*{customMessage}*");
    }

    [Fact]
    public async Task Transaction_RollsBackAllOperations_WhenLaterOperationFails()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from _ in add(new User { Name = "First", Email = "first-rollback@test.com" })
            from __ in saveChanges
            from ___ in add(new User { Name = "Second", Email = "second-rollback@test.com" })
            from ____ in saveChanges
            from _____ in fail<Unit>("Failure after both inserts")
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Both users should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var firstUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "first-rollback@test.com");
        var secondUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "second-rollback@test.com");
        firstUser.Should().BeNull();
        secondUser.Should().BeNull();
    }

    [Fact]
    public async Task Transact_ReturnsValue_OnSuccess()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from entry in add(new User { Name = "ReturnValue", Email = "returnvalue@test.com" })
            from _ in saveChanges
            select entry.Entity.Id
        );

        var id = await query.Run(env).RunAsync();
        id.Should().BeGreaterThan(0);

        // Verify user exists
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.FindAsync(id);
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task Transact_PropagatesError_AfterRollback()
    {
        var env = _fixture.CreatePgEnv();
        var errorMessage = "Intentional error for propagation test";

        var query = transact(
            from _ in add(new User { Name = "PropagateError", Email = "propagate@test.com" })
            from __ in saveChanges
            from ___ in fail<Unit>(errorMessage)
            select unit
        );

        // Error should be propagated to caller
        var act = async () => await query.Run(env).RunAsync();
        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Message.Should().Contain(errorMessage);

        // And transaction should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "propagate@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task Transact_NestedTransact_InnerFailureRollsBackOuter()
    {
        var env = _fixture.CreatePgEnv();

        // Note: PostgreSQL doesn't support true nested transactions, but EF Core uses savepoints
        // This tests that inner failure causes outer to also fail
        var query = transact(
            from _ in add(new User { Name = "Outer", Email = "outer-nested@test.com" })
            from __ in saveChanges
            from ___ in transact(
                from ____ in add(new User { Name = "Inner", Email = "inner-nested@test.com" })
                from _____ in saveChanges
                from ______ in fail<Unit>("Inner transaction failure")
                select unit
            )
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Both outer and inner should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var outerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "outer-nested@test.com");
        var innerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "inner-nested@test.com");
        outerUser.Should().BeNull();
        innerUser.Should().BeNull();
    }
}

/// <summary>
/// Tests for MonadUnliftIO correctness - ToIO extraction and MapIO transformation.
/// </summary>
[Collection("PostgreSQL")]
public class PgMonadUnliftIOTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgMonadUnliftIOTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ToIO_ExtractsIO_ThatCanBeRunIndependently()
    {
        var env = _fixture.CreatePgEnv();

        // Create a Pg computation
        var pgOp =
            from _ in add(new User { Name = "ToIOTest", Email = "toio@test.com" })
            from __ in saveChanges
            select 42;

        // Extract the IO using ToIO
        var pgWithIO = Pg.ToIO(pgOp).As();

        // Run the Pg to get the IO
        var extractedIO = await pgWithIO.Run(env).RunAsync();

        // The extracted IO can be run independently
        var result = await extractedIO.RunAsync();
        result.Should().Be(42);

        // Verify the user was created
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "toio@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task ToIO_CapturesEnvironment_FromEnclosingPg()
    {
        var env = _fixture.CreatePgEnv();

        // ToIO should capture the environment from the enclosing Pg context
        var query =
            from e in Pg.env
            from ioWrapper in Pg.ToIO(
                from ctx in context
                select ctx.GetType().Name
            ).As()
            select ioWrapper;

        var extractedIO = await query.Run(env).RunAsync();
        var result = await extractedIO.RunAsync();

        // Should have captured the DbContext type name
        result.Should().Contain("DbContext");
    }

    [Fact]
    public async Task ToIO_PreservesErrorSemantics()
    {
        var env = _fixture.CreatePgEnv();
        var errorMessage = "ToIO error test";

        var pgOp = fail<int>(errorMessage);
        var pgWithIO = Pg.ToIO(pgOp).As();

        var extractedIO = await pgWithIO.Run(env).RunAsync();

        // Running the extracted IO should throw the same error
        var act = async () => await extractedIO.RunAsync();
        await act.Should().ThrowAsync<Exception>()
            .WithMessage($"*{errorMessage}*");
    }

    [Fact]
    public async Task ToIO_AllowsIOTransformation()
    {
        var env = _fixture.CreatePgEnv();

        // Start with a Pg computation
        var pgOp = pure(10);

        // Use ToIO to extract and transform the underlying IO
        var transformed =
            from io in Pg.ToIO(pgOp).As()
            from transformedResult in Pg.liftIO(io.Map(x => x * 2))
            select transformedResult;

        var result = await transformed.Run(env).RunAsync();
        result.Should().Be(20);
    }

    [Fact]
    public async Task ToIO_AllowsIOLevelCatching()
    {
        var env = _fixture.CreatePgEnv();

        var pgOp = fail<int>("ToIO catch test");

        // Use ToIO to extract IO, then apply IO-level catching
        var withCatch =
            from io in Pg.ToIO(pgOp).As()
            from catchResult in Pg.liftIO(io.Catch(_ => true, _ => LanguageExt.IO.pure(999)))
            select catchResult;

        var result = await withCatch.Run(env).RunAsync();
        result.Should().Be(999);
    }

    [Fact]
    public async Task ToIO_WorksWithDatabaseOperations()
    {
        var env = _fixture.CreatePgEnv();

        // Setup: add a user
        await (
            from _ in add(new User { Name = "ToIODb", Email = "toiodb@test.com", Balance = 100 })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Extract IO from a database query
        var query =
            from ioWrapper in Pg.ToIO(
                from users in set<User>()
                from user in head(users.Where(u => u.Email == "toiodb@test.com"))
                select user.Map(u => u.Balance)
            ).As()
            select ioWrapper;

        var extractedIO = await query.Run(env).RunAsync();
        var result = await extractedIO.RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(b => b.Should().Be(100));
    }
}

/// <summary>
/// Tests for bracket-like patterns with Pg monad using ToIO + Catch pattern.
/// This demonstrates that Pg computations work correctly with resource management patterns.
/// </summary>
[Collection("PostgreSQL")]
public class PgBracketTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgBracketTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Helper method implementing bracket pattern using ToIO + Catch (same pattern as transact)
    /// </summary>
    private static Pg<B> Bracket<A, B>(Pg<A> acquire, Func<A, Pg<B>> use, Func<A, Pg<Unit>> release) =>
        from resource in acquire
        from operationIO in Pg.ToIO(use(resource)).As()
        from result in Pg.liftIO(
            operationIO.Catch(
                _ => true,
                err => release(resource).Run(new PgEnv(null!)) // Release on error
                    .Bind(_ => LanguageExt.IO.fail<B>(err))
            )
        )
        from _ in release(resource) // Release on success
        select result;

    [Fact]
    public async Task BracketPattern_ReleasesResource_OnSuccess()
    {
        var env = _fixture.CreatePgEnv();
        var resourceAcquired = false;
        var resourceReleased = false;

        // Use custom bracket pattern with Pg computation
        var bracketed =
            from resource in Pg.liftIO(() => { resourceAcquired = true; return "resource"; })
            from opIO in Pg.ToIO(
                from _ in add(new User { Name = "BracketSuccess", Email = "bracket-success@test.com" })
                from __ in saveChanges
                select resource.Length
            ).As()
            from opResult in Pg.liftIO(opIO)
            from ___ in Pg.liftIO(() => { resourceReleased = true; return unit; })
            select opResult;

        var result = await bracketed.Run(env).RunAsync();

        resourceAcquired.Should().BeTrue();
        resourceReleased.Should().BeTrue();
        result.Should().Be(8); // "resource".Length

        // Verify the DB operation succeeded
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "bracket-success@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task BracketPattern_ReleasesResource_OnFailure_UsingCatch()
    {
        var env = _fixture.CreatePgEnv();
        var resourceAcquired = false;
        var resourceReleased = false;

        // Demonstrate bracket pattern with error handling using ToIO + Catch
        var operation =
            from _ in Pg.liftIO(() => { resourceAcquired = true; return unit; })
            from opIO in Pg.ToIO(
                from __ in add(new User { Name = "BracketFail", Email = "bracket-fail@test.com" })
                from ___ in saveChanges
                from ____ in fail<Unit>("Intentional failure inside bracket")
                select unit
            ).As()
            from result in Pg.liftIO(
                opIO.Catch(
                    _ => true,
                    err =>
                    {
                        resourceReleased = true;
                        return LanguageExt.IO.fail<Unit>(err);
                    }
                )
            )
            select result;

        var act = async () => await operation.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        resourceAcquired.Should().BeTrue();
        resourceReleased.Should().BeTrue(); // Resource should be released on error!
    }

    [Fact]
    public async Task TransactAsBuiltInBracket_CommitsOnSuccess()
    {
        var env = _fixture.CreatePgEnv();

        // transact is itself a bracket pattern - demonstrate it works
        var query = transact(
            from _ in add(new User { Name = "TransactBracket", Email = "transact-bracket@test.com" })
            from __ in saveChanges
            select 42
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(42);

        // Verify transaction committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "transact-bracket@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task TransactAsBuiltInBracket_RollsBackOnFailure()
    {
        var env = _fixture.CreatePgEnv();

        var query = transact(
            from _ in add(new User { Name = "TransactBracketFail", Email = "transact-bracket-fail@test.com" })
            from __ in saveChanges
            from ___ in fail<int>("Failure inside transact")
            select 0
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Transaction should have rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "transact-bracket-fail@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task WithAdvisoryLockAsBuiltInBracket_ReleasesOnSuccess()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 555555L;

        // withAdvisoryLock is also a bracket pattern
        var query = withAdvisoryLock(lockKey,
            from _ in add(new User { Name = "LockBracket", Email = "lock-bracket@test.com" })
            from __ in saveChanges
            select 99
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(99);

        // Verify lock was released
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task WithAdvisoryLockAsBuiltInBracket_ReleasesOnFailure()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 666666L;

        var query = withAdvisoryLock(lockKey,
            from _ in add(new User { Name = "LockBracketFail", Email = "lock-bracket-fail@test.com" })
            from __ in fail<Unit>("Failure inside lock")
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Lock should still be released even on error
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task UsePattern_WithDisposable_DisposesOnCompletion()
    {
        var env = _fixture.CreatePgEnv();
        var disposable = new TestDisposable();

        // Implement use pattern manually with try/finally semantics via Catch
        var operation =
            from opIO in Pg.ToIO(
                from _ in add(new User { Name = "UseDisposable", Email = "use-disposable@test.com" })
                from __ in saveChanges
                select disposable.IsDisposed
            ).As()
            from result in Pg.liftIO(
                opIO.Map(r =>
                {
                    disposable.Dispose();
                    return r;
                }).Catch(
                    _ => true,
                    err =>
                    {
                        disposable.Dispose();
                        return LanguageExt.IO.fail<bool>(err);
                    }
                )
            )
            select result;

        var wasDisposedDuringUse = await operation.Run(env).RunAsync();
        wasDisposedDuringUse.Should().BeFalse(); // Not disposed during use
        disposable.IsDisposed.Should().BeTrue(); // Disposed after use completes
    }

    [Fact]
    public async Task UsePattern_WithDisposable_DisposesOnError()
    {
        var env = _fixture.CreatePgEnv();
        var disposable = new TestDisposable();

        var operation =
            from opIO in Pg.ToIO(
                from _ in add(new User { Name = "UseDisposableErr", Email = "use-disposable-err@test.com" })
                from __ in fail<Unit>("Error during use")
                select unit
            ).As()
            from result in Pg.liftIO(
                opIO.Map(r =>
                {
                    disposable.Dispose();
                    return r;
                }).Catch(
                    _ => true,
                    err =>
                    {
                        disposable.Dispose();
                        return LanguageExt.IO.fail<Unit>(err);
                    }
                )
            )
            select result;

        var act = async () => await operation.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Resource should still be disposed even on error
        disposable.IsDisposed.Should().BeTrue();
    }

    private class TestDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}

/// <summary>
/// Tests for nested resource patterns - combining transactions and locks.
/// </summary>
[Collection("PostgreSQL")]
public class PgNestedResourceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgNestedResourceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TransactionWithAdvisoryLock_BothReleaseOnSuccess()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 111111L;

        var query = transact(
            from _ in withAdvisoryLock(lockKey,
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
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task TransactionWithAdvisoryLock_BothReleaseOnFailure()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 222222L;

        var query = transact(
            from _ in withAdvisoryLock(lockKey,
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
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task AdvisoryLockWithTransaction_BothReleaseOnSuccess()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 333333L;

        // Lock wrapping transaction (opposite nesting)
        var query = withAdvisoryLock(lockKey,
            transact(
                from _ in add(new User { Name = "LockTx", Email = "locktx@test.com" })
                from __ in saveChanges
                select unit
            )
        );

        await query.Run(env).RunAsync();

        // Verify transaction committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "locktx@test.com");
        user.Should().NotBeNull();

        // Verify lock was released
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task AdvisoryLockWithTransaction_BothReleaseOnFailure()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var lockKey = 444444L;

        var query = withAdvisoryLock(lockKey,
            transact(
                from _ in add(new User { Name = "LockTxFail", Email = "locktxfail@test.com" })
                from __ in saveChanges
                from ___ in fail<Unit>("Failure inside transaction inside lock")
                select unit
            )
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Transaction rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "locktxfail@test.com");
        user.Should().BeNull();

        // Lock released
        var env2 = _fixture.CreatePgEnvWithConnection();
        var canAcquire = await tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        canAcquire.Should().BeTrue();
        await advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task NestedResources_AllReleasedOnSuccess()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var resource1Released = false;
        var resource2Released = false;

        // Nested resource pattern using ToIO + Catch
        var query =
            from _ in Pg.liftIO(() => { return "outer"; })
            from outerOpIO in Pg.ToIO(
                from __ in Pg.liftIO(() => { return "inner"; })
                from innerOpIO in Pg.ToIO(
                    from ___ in add(new User { Name = "NestedRes", Email = "nestedres@test.com" })
                    from ____ in saveChanges
                    select "outerinner"
                ).As()
                from innerResult in Pg.liftIO(innerOpIO.Map(r => { resource2Released = true; return r; }))
                select innerResult
            ).As()
            from outerResult in Pg.liftIO(outerOpIO.Map(r => { resource1Released = true; return r; }))
            select outerResult;

        var result = await query.Run(env).RunAsync();
        result.Should().Be("outerinner");
        resource1Released.Should().BeTrue();
        resource2Released.Should().BeTrue();
    }

    [Fact]
    public async Task NestedResources_AllReleasedOnFailure()
    {
        var env = _fixture.CreatePgEnvWithConnection();
        var resource1Released = false;
        var resource2Released = false;

        // Nested resource pattern with failure
        var query =
            from outerOpIO in Pg.ToIO(
                from innerOpIO in Pg.ToIO(
                    from _ in add(new User { Name = "NestedResFail", Email = "nestedresfail@test.com" })
                    from __ in fail<string>("Inner failure")
                    select ""
                ).As()
                from innerResult in Pg.liftIO(
                    innerOpIO.Catch(_ => true, err =>
                    {
                        resource2Released = true;
                        return LanguageExt.IO.fail<string>(err);
                    })
                )
                select innerResult
            ).As()
            from outerResult in Pg.liftIO(
                outerOpIO.Catch(_ => true, err =>
                {
                    resource1Released = true;
                    return LanguageExt.IO.fail<string>(err);
                })
            )
            select outerResult;

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Both resources should be released even on failure
        resource1Released.Should().BeTrue();
        resource2Released.Should().BeTrue();
    }

    [Fact]
    public async Task TransactionInsideCustomBracket_ResourcesReleasedCorrectly()
    {
        var env = _fixture.CreatePgEnv();
        var bracketReleased = false;

        // Custom bracket wrapping transact
        var query =
            from _ in Pg.liftIO(() => "bracket-resource")
            from opIO in Pg.ToIO(
                transact(
                    from __ in add(new User { Name = "TxInBracket", Email = "txinbracket@test.com" })
                    from ___ in saveChanges
                    select 16
                )
            ).As()
            from opResult in Pg.liftIO(opIO.Map(r => { bracketReleased = true; return r; }))
            select opResult;

        var result = await query.Run(env).RunAsync();
        result.Should().Be(16);
        bracketReleased.Should().BeTrue();

        // Transaction committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "txinbracket@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task CustomBracketInsideTransaction_ResourcesReleasedCorrectly()
    {
        var env = _fixture.CreatePgEnv();
        var bracketReleased = false;

        // Transaction wrapping custom bracket pattern
        var query = transact(
            from _ in Pg.liftIO(() => "inner-bracket")
            from opIO in Pg.ToIO(
                from __ in add(new User { Name = "BracketInTx", Email = "bracketintx@test.com" })
                from ___ in saveChanges
                select 13
            ).As()
            from opResult in Pg.liftIO(opIO.Map(r => { bracketReleased = true; return r; }))
            select opResult
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(13);
        bracketReleased.Should().BeTrue();

        // Transaction committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "bracketintx@test.com");
        user.Should().NotBeNull();
    }
}
