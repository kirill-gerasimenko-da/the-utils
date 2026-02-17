namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for resource management and disposal patterns in the Db monad.
/// </summary>
[Collection("PostgreSQL")]
public class DbResourceTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbResourceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== DbRT Creation ====================

    [Fact]
    public void DbRT_FromContext_CreatesValidEnv()
    {
        var context = _fixture.CreateDbContext();
        var env = DbRT.FromContext(context);

        env.Context.Should().Be(context);
        env.DefaultIsolation.IsNone.Should().BeTrue();
        env.CommandTimeout.IsNone.Should().BeTrue();
    }

    // ==================== Context Lifecycle ====================

    [Fact]
    public async Task MultipleOperations_SameEnv_ReuseContext()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from ctx1 in context
            from _ in add(new User { Name = "First", Email = "first@test.com" })
            from ctx2 in context
            select ctx1 == ctx2;

        var result = await query.RunIO(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleOperations_DifferentEnvs_DifferentContexts()
    {
        var env1 = _fixture.CreateDbRT();
        var env2 = _fixture.CreateDbRT();

        var ctx1 = await context.RunIO(env1).RunAsync();
        var ctx2 = await context.RunIO(env2).RunAsync();

        ctx1.Should().NotBeSameAs(ctx2);
    }

    // ==================== Transaction Resource Cleanup ====================

    [Fact]
    public async Task Transaction_OnSuccess_CleansUpTransaction()
    {
        var env = _fixture.CreateDbRT();

        // Transaction should be cleaned up after successful completion
        var query = transact(
            from _ in add(new User { Name = "TxCleanup", Email = "txcleanup@test.com" })
            from __ in saveChanges
            select unit
        );

        await query.RunIO(env).RunAsync();

        // After transact completes, no active transaction should remain
        var hasTx = await currentTransaction.RunIO(env).RunAsync();
        hasTx.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_OnFailure_CleansUpTransaction()
    {
        var env = _fixture.CreateDbRT();

        var query = transact(
            from _ in add(new User { Name = "TxFailCleanup", Email = "txfailcleanup@test.com" })
            from __ in saveChanges
            from ___ in fail<Unit>("Force cleanup")
            select unit
        );

        var act = async () => await query.RunIO(env).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // After failed transact, no active transaction should remain
        var hasTx = await currentTransaction.RunIO(env).RunAsync();
        hasTx.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task ManualTransaction_AfterCommit_NoActiveTransaction()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "ManualCommitCleanup", Email = "manualcommitcleanup@test.com" })
            from __ in saveChanges
            from ___ in commit
            from afterTx in currentTransaction
            select afterTx.IsNone;

        var result = await query.RunIO(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ManualTransaction_AfterRollback_NoActiveTransaction()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "ManualRollbackCleanup", Email = "manualrollbackcleanup@test.com" })
            from __ in rollback
            from afterTx in currentTransaction
            select afterTx.IsNone;

        var result = await query.RunIO(env).RunAsync();
        result.Should().BeTrue();
    }

    // ==================== Connection State ====================

    [Fact]
    public async Task Operations_WithClosedConnection_OpensAutomatically()
    {
        var context = _fixture.CreateDbContext();
        var connection = context.Database.GetDbConnection();

        // Ensure connection is closed before operation
        if (connection.State != System.Data.ConnectionState.Closed)
            await connection.CloseAsync();

        var env = new DbRT(context);

        // EF Core should open the connection automatically
        var query =
            from _ in add(new User { Name = "AutoOpen", Email = "autoopen@test.com" })
            from __ in saveChanges
            select unit;

        await query.RunIO(env).RunAsync();

        // Verify operation succeeded
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "autoopen@test.com");
        user.Should().NotBeNull();
    }

    // ==================== DbRT Configuration Options ====================

    [Fact]
    public void DbRT_WithCommandTimeout_StoresTimeout()
    {
        var context = _fixture.CreateDbContext();
        var timeout = TimeSpan.FromSeconds(60);
        var env = new DbRT(context, CommandTimeout: timeout);

        env.CommandTimeout.IsSome.Should().BeTrue();
        env.CommandTimeout.IfNone(TimeSpan.Zero).Should().Be(timeout);
    }

    [Fact]
    public void DbRT_WithDefaultIsolation_StoresIsolation()
    {
        var context = _fixture.CreateDbContext();
        var env = new DbRT(context, DefaultIsolation: System.Data.IsolationLevel.Serializable);

        env.DefaultIsolation.IsSome.Should().BeTrue();
        env.DefaultIsolation.IfNone(System.Data.IsolationLevel.Unspecified)
            .Should().Be(System.Data.IsolationLevel.Serializable);
    }

    // ==================== Sequential Operations ====================

    [Fact]
    public async Task SequentialOperations_SameEnv_MaintainState()
    {
        var env = _fixture.CreateDbRT();

        // First operation
        await add(new User { Name = "Seq1", Email = "seq1@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Second operation using same env
        var count = await Db.count(env.Context.Set<User>().Where(u => u.Email.StartsWith("seq")))
            .RunIO(env).RunAsync();

        count.Should().Be(1);

        // Third operation
        await add(new User { Name = "Seq2", Email = "seq2@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env).RunAsync();

        // Fourth operation
        var finalCount = await Db.count(env.Context.Set<User>().Where(u => u.Email.StartsWith("seq")))
            .RunIO(env).RunAsync();

        finalCount.Should().Be(2);
    }

    [Fact]
    public async Task ParallelOperations_DifferentEnvs_Isolated()
    {
        var env1 = _fixture.CreateDbRT();
        var env2 = _fixture.CreateDbRT();

        // Both envs see independent contexts
        var op1 = add(new User { Name = "Parallel1", Email = "parallel1@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env1).RunAsync().AsTask();

        var op2 = add(new User { Name = "Parallel2", Email = "parallel2@test.com" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .RunIO(env2).RunAsync().AsTask();

        await Task.WhenAll(op1, op2);

        // Both should be committed independently
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("parallel"));
        count.Should().Be(2);
    }
}
