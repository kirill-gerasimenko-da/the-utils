namespace TheUtils.DbTests;

using System.Data;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for transaction edge cases and advanced scenarios.
/// </summary>
[Collection("PostgreSQL")]
public class DbTransactionEdgeCaseTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbTransactionEdgeCaseTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Nested Transaction Behavior ====================
    // Note: PostgreSQL doesn't support true nested transactions (would need savepoints)
    // These tests are skipped for PostgreSQL compatibility

    [Fact(Skip = "PostgreSQL doesn't support nested transactions without savepoints")]
    public async Task NestedTransact_InnerFails_OuterRollsBack()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in add(new User { Name = "Outer", Email = "nested_outer@test.com" })
            from __ in saveChanges
            from ___ in transact(
                from ____ in add(new User { Name = "Inner", Email = "nested_inner@test.com" })
                from _____ in saveChanges
                from ______ in fail<Unit>("Inner failure")
                select unit
            )
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Inner failure*");

        // Both outer and inner should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var outerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "nested_outer@test.com");
        var innerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "nested_inner@test.com");

        outerUser.Should().BeNull();
        innerUser.Should().BeNull();
    }

    [Fact(Skip = "PostgreSQL doesn't support nested transactions without savepoints")]
    public async Task NestedTransact_OuterFails_AllRollback()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in add(new User { Name = "Outer", Email = "outer_fail@test.com" })
            from __ in saveChanges
            from ___ in transact(
                from ____ in add(new User { Name = "Inner", Email = "inner_fail@test.com" })
                from _____ in saveChanges
                select unit
            )
            from ______ in fail<Unit>("Outer failure after inner succeeds")
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Outer failure*");

        // Both should be rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var outerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "outer_fail@test.com");
        var innerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "inner_fail@test.com");

        outerUser.Should().BeNull();
        innerUser.Should().BeNull();
    }

    [Fact(Skip = "PostgreSQL doesn't support nested transactions without savepoints")]
    public async Task NestedTransact_BothSucceed_AllCommitted()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in add(new User { Name = "Outer", Email = "nested_success_outer@test.com" })
            from __ in saveChanges
            from ___ in transact(
                from ____ in add(new User { Name = "Inner", Email = "nested_success_inner@test.com" })
                from _____ in saveChanges
                select unit
            )
            select unit
        );

        await query.Run(env).RunAsync();

        // Both should be committed
        await using var verifyContext = _fixture.CreateDbContext();
        var outerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "nested_success_outer@test.com");
        var innerUser = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "nested_success_inner@test.com");

        outerUser.Should().NotBeNull();
        innerUser.Should().NotBeNull();
    }

    // ==================== Isolation Level Tests ====================

    [Fact]
    public async Task TransactWithIsolationLevel_UsesSpecifiedLevel()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from tx in currentTransaction
            select tx.IsSome,
            IsolationLevel.Serializable
        );

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BeginTransaction_WithIsolationLevel_StartsCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from tx in beginTransaction(IsolationLevel.ReadCommitted)
            from hasTx in currentTransaction.Map(t => t.IsSome)
            from _ in commit
            select hasTx;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DbEnv_DefaultIsolation_IsUsedByTransaction()
    {
        var context = _fixture.CreateDbContext();
        var env = new DbEnv(context, DefaultIsolation: IsolationLevel.Serializable);

        var query = transact(
            from tx in currentTransaction
            select tx.IsSome
        );

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    // ==================== Transaction State Tracking ====================

    [Fact]
    public async Task CurrentTransaction_InsideTransaction_ReturnsSome()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from tx in currentTransaction
            select tx.IsSome
        );

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CurrentTransaction_OutsideTransaction_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();

        var result = await currentTransaction.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task CurrentTransaction_AfterCommit_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "TxTest", Email = "txtest@test.com" })
            from __ in saveChanges
            from ___ in commit
            from afterCommit in currentTransaction
            select afterCommit.IsNone;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CurrentTransaction_AfterRollback_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "RollbackTest", Email = "rollbacktest@test.com" })
            from __ in saveChanges
            from ___ in rollback
            from afterRollback in currentTransaction
            select afterRollback.IsNone;

        var result = await query.Run(env).RunAsync();
        result.Should().BeTrue();
    }

    // ==================== Manual Transaction Control ====================

    [Fact]
    public async Task ManualBeginCommit_CommitsData()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from tx in beginTransaction()
            from _ in add(new User { Name = "ManualTx", Email = "manualtx@test.com" })
            from __ in saveChanges
            from ___ in commit
            select unit;

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "manualtx@test.com");
        user.Should().NotBeNull();
    }

    [Fact]
    public async Task ManualBeginRollback_DoesNotCommitData()
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

    // ==================== Multiple Operations in Transaction ====================

    [Fact]
    public async Task Transaction_MultipleInserts_AllOrNothing()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in add(new User { Name = "Multi1", Email = "multi1@test.com" })
            from __ in add(new User { Name = "Multi2", Email = "multi2@test.com" })
            from ___ in add(new User { Name = "Multi3", Email = "multi3@test.com" })
            from ____ in saveChanges
            select unit
        );

        await query.Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("multi"));
        count.Should().Be(3);
    }

    [Fact]
    public async Task Transaction_PartialFailure_RollsBackAll()
    {
        var env = _fixture.CreateDbEnv();

        // First insert outside transaction (will be committed)
        await (
            from _ in add(new User { Name = "Existing", Email = "partial1@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Try to insert more in transaction with duplicate
        var env2 = _fixture.CreateDbEnv();
        var query = transact(
            from _ in add(new User { Name = "New1", Email = "partial2@test.com" })
            from __ in add(new User { Name = "Duplicate", Email = "partial1@test.com" }) // duplicate
            from ___ in saveChanges
            select unit
        );

        var act = async () => await query.Run(env2).RunAsync();

        await act.Should().ThrowAsync<DbUpdateException>();

        // Only the original should exist
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("partial"));
        count.Should().Be(1);
    }

    // ==================== Transaction Return Values ====================

    [Fact]
    public async Task Transact_ReturnsValueOnSuccess()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from entry in add(new User { Name = "ReturnId", Email = "returnid@test.com" })
            from _ in saveChanges
            select entry.Entity.Id
        );

        var id = await query.Run(env).RunAsync();
        id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Transact_ReturnsComputedValue()
    {
        var env = _fixture.CreateDbEnv();

        var query = transact(
            from _ in add(new User { Name = "Computed1", Email = "computed1@test.com" })
            from __ in add(new User { Name = "Computed2", Email = "computed2@test.com" })
            from ___ in saveChanges
            from c in count(env.Context.Set<User>().Where(u => u.Email.StartsWith("computed")))
            select c
        );

        var result = await query.Run(env).RunAsync();
        result.Should().Be(2);
    }
}
