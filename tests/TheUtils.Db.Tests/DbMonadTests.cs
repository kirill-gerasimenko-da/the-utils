namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for the Db monad core functionality.
/// </summary>
[Collection("PostgreSQL")]
public class DbMonadTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbMonadTests(PostgreSqlFixture fixture)
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
        var env = _fixture.CreateDbEnv();
        var value = 42;
        Func<int, Db<int>> f = x => pure(x * 2);

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
        var env = _fixture.CreateDbEnv();
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
        var env = _fixture.CreateDbEnv();
        var m = pure(10);
        Func<int, Db<int>> f = x => pure(x + 5);
        Func<int, Db<int>> g = x => pure(x * 2);

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

        var query =
            from e in Db.env
            select e.Context != null;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Environment_CanAccessDefaultIsolation()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from e in Db.env
            select e.DefaultIsolation;

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    // ==================== DbEnv Option Defaults ====================

    [Fact]
    public void DbEnv_DefaultRawConnection_UsesContextConnection()
    {
        var context = _fixture.CreateDbContext();
        var env = new DbEnv(context);

        env.RawConnection.IsNone.Should().BeTrue();
        env.Connection.Should().NotBeNull();
    }

    [Fact]
    public void DbEnv_DefaultCommandTimeout_IsNone()
    {
        var context = _fixture.CreateDbContext();
        var env = new DbEnv(context);

        env.CommandTimeout.IsNone.Should().BeTrue();
    }

    [Fact]
    public void DbEnv_ExplicitCommandTimeout_ReturnsValue()
    {
        var context = _fixture.CreateDbContext();
        var timeout = TimeSpan.FromSeconds(30);
        var env = new DbEnv(context, CommandTimeout: timeout);

        env.CommandTimeout.IsSome.Should().BeTrue();
        env.CommandTimeout.IfNone(TimeSpan.Zero).Should().Be(timeout);
    }

    // ==================== Error Handling ====================

    [Fact]
    public async Task Fail_PropagatesError()
    {
        var env = _fixture.CreateDbEnv();

        var query = fail<int>("Test error");

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Test error*");
    }

    [Fact]
    public async Task Catch_HandlesError()
    {
        var env = _fixture.CreateDbEnv();

        var query = Db.Catch(
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
        var env = _fixture.CreateDbEnv();

        var query = Db.Catch(
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
        var env = _fixture.CreateDbEnv();

        var query =
            from f in Db.facade
            select f != null;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    // ==================== LiftIO Operations ====================

    [Fact]
    public async Task LiftIO_WithAsyncFunc_ExecutesOperation()
    {
        var env = _fixture.CreateDbEnv();
        var executed = false;

        var query = Db.liftIO<int>(async _ =>
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
        var env = _fixture.CreateDbEnv();

        var query = Db.liftIO(() => 123);

        var result = await query.Run(env).RunAsync();
        result.Should().Be(123);
    }

    [Fact]
    public async Task LiftIO_WithIO_ExecutesOperation()
    {
        var env = _fixture.CreateDbEnv();

        var io = LanguageExt.IO.lift(() => "test");
        var query = Db.liftIO(io);

        var result = await query.Run(env).RunAsync();
        result.Should().Be("test");
    }
}

/// <summary>
/// Tests for Db monad database operations.
/// </summary>
[Collection("PostgreSQL")]
public class DbDatabaseOperationsTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbDatabaseOperationsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== CRUD Operations ====================

    [Fact]
    public async Task Add_InsertsEntity()
    {
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();
        var users = Seq(
            new User { Name = "Bob", Email = "bob@test.com" },
            new User { Name = "Carol", Email = "carol@test.com" }
        );

        var query =
            from _ in addRange(users)
            from __ in saveChanges
            from c in Db.count(env.Context.Set<User>())
            select c;

        var result = await query.Run(env).RunAsync();
        result.Should().Be(2);
    }

    [Fact]
    public async Task Update_ModifiesEntity()
    {
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

        await add(new User { Name = "HeadT", Email = "headt@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // headT returns OptionT - chain with Map then Run to get Db<Option<A>>
        var query = headT(env.Context.Set<User>().Where(u => u.Email == "headt@test.com"))
            .Map(u => u.Name)
            .Run()
            .As();

        var result = await query.Run(env).RunAsync();
        result.IsSome.Should().BeTrue();
        result.IfSome(name => name.Should().Be("HeadT"));
    }

    [Fact]
    public async Task Any_ChecksExistence()
    {
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
}

/// <summary>
/// Tests for Db monad transaction support.
/// </summary>
[Collection("PostgreSQL")]
public class DbTransactionTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbTransactionTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Transaction_CommitsOnSuccess()
    {
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

        var query =
            from tx in beginTransaction()
            from hasTx in currentTransaction.Map(t => t.IsSome)
            from _ in commit
            select hasTx;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Commit_CommitsCurrentTransaction()
    {
        var env = _fixture.CreateDbEnv();

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
        var env = _fixture.CreateDbEnv();

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

    [Fact]
    public async Task Transact_ReturnsValue_OnSuccess()
    {
        var env = _fixture.CreateDbEnv();

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
}

/// <summary>
/// Tests for MonadUnliftIO correctness.
/// </summary>
[Collection("PostgreSQL")]
public class DbMonadUnliftIOTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbMonadUnliftIOTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ToIO_ExtractsIO_ThatCanBeRunIndependently()
    {
        var env = _fixture.CreateDbEnv();

        // Create a Db computation
        var dbOp =
            from _ in add(new User { Name = "ToIOTest", Email = "toio@test.com" })
            from __ in saveChanges
            select 42;

        // Extract the IO using ToIO
        var dbWithIO = Db.ToIO(dbOp).As();

        // Run the Db to get the IO
        var extractedIO = await dbWithIO.Run(env).RunAsync();

        // The extracted IO can be run independently
        var result = await extractedIO.RunAsync();
        result.Should().Be(42);

        // Verify the user was created
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "toio@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task ToIO_CapturesEnvironment_FromEnclosingDb()
    {
        var env = _fixture.CreateDbEnv();

        // ToIO should capture the environment from the enclosing Db context
        var query =
            from e in Db.env
            from ioWrapper in Db.ToIO(
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
        var env = _fixture.CreateDbEnv();
        var errorMessage = "ToIO error test";

        var dbOp = fail<int>(errorMessage);
        var dbWithIO = Db.ToIO(dbOp).As();

        var extractedIO = await dbWithIO.Run(env).RunAsync();

        // Running the extracted IO should throw the same error
        var act = async () => await extractedIO.RunAsync();
        await act.Should().ThrowAsync<Exception>()
            .WithMessage($"*{errorMessage}*");
    }

    [Fact]
    public async Task ToIO_AllowsIOTransformation()
    {
        var env = _fixture.CreateDbEnv();

        // Start with a Db computation
        var dbOp = pure(10);

        // Use ToIO to extract and transform the underlying IO
        var transformed =
            from io in Db.ToIO(dbOp).As()
            from transformedResult in Db.liftIO(io.Map(x => x * 2))
            select transformedResult;

        var result = await transformed.Run(env).RunAsync();
        result.Should().Be(20);
    }

    [Fact]
    public async Task ToIO_AllowsIOLevelCatching()
    {
        var env = _fixture.CreateDbEnv();

        var dbOp = fail<int>("ToIO catch test");

        // Use ToIO to extract IO, then apply IO-level catching
        var withCatch =
            from io in Db.ToIO(dbOp).As()
            from catchResult in Db.liftIO(io.Catch(_ => true, _ => LanguageExt.IO.pure(999)))
            select catchResult;

        var result = await withCatch.Run(env).RunAsync();
        result.Should().Be(999);
    }
}
