namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for error handling and recovery in the Db monad.
/// </summary>
[Collection("PostgreSQL")]
public class DbErrorHandlingTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbErrorHandlingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Constraint Violations ====================

    [Fact]
    public async Task SaveChanges_UniqueConstraintViolation_ThrowsDbUpdateException()
    {
        var env = _fixture.CreateDbEnv();

        // Insert first user
        await (
            from _ in add(new User { Name = "First", Email = "unique@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Try to insert duplicate email (unique constraint violation)
        var env2 = _fixture.CreateDbEnv();
        var query =
            from _ in add(new User { Name = "Second", Email = "unique@test.com" })
            from __ in saveChanges
            select unit;

        var act = async () => await query.Run(env2).RunAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_UniqueConstraintViolation_InTransaction_RollsBack()
    {
        var env = _fixture.CreateDbEnv();

        // Insert first user
        await (
            from _ in add(new User { Name = "First", Email = "txunique@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Try to insert duplicate in transaction
        var env2 = _fixture.CreateDbEnv();
        var query = transact(
            from _ in add(new User { Name = "Second", Email = "txunique@test.com" })
            from __ in saveChanges
            select unit
        );

        var act = async () => await query.Run(env2).RunAsync();

        await act.Should().ThrowAsync<DbUpdateException>();

        // Verify no new user was added
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email == "txunique@test.com");
        count.Should().Be(1); // Only the first one
    }

    // ==================== Fail Propagation ====================

    [Fact]
    public async Task Fail_WithMessage_PropagatesErrorWithCorrectMessage()
    {
        var env = _fixture.CreateDbEnv();
        var errorMessage = "Custom error message for testing";

        var query = fail<int>(errorMessage);

        var act = async () => await query.Run(env).RunAsync();

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Message.Should().Contain(errorMessage);
    }

    [Fact]
    public async Task Fail_InTransaction_RollsBackAndPropagatesError()
    {
        var env = _fixture.CreateDbEnv();
        var errorMessage = "Intentional transaction failure";

        var query = transact(
            from _ in add(new User { Name = "WillRollback", Email = "fail@test.com" })
            from __ in saveChanges
            from ___ in fail<Unit>(errorMessage)
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();

        // Error is propagated
        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Message.Should().Contain(errorMessage);

        // Transaction rolled back
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "fail@test.com");
        user.Should().BeNull();
    }

    [Fact]
    public async Task Fail_ChainedOperations_StopsAtFailure()
    {
        var env = _fixture.CreateDbEnv();
        var operationsExecuted = new List<string>();

        var query =
            from _ in liftIO(() =>
            {
                operationsExecuted.Add("first");
                return unit;
            })
            from __ in fail<Unit>("Stop here")
            from ___ in liftIO(() =>
            {
                operationsExecuted.Add("should_not_execute");
                return unit;
            })
            select unit;

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>();
        operationsExecuted.Should().ContainSingle().Which.Should().Be("first");
    }

    // ==================== Catch Recovery ====================

    [Fact]
    public async Task Catch_WithMatchingPredicate_RecoverFromError()
    {
        var env = _fixture.CreateDbEnv();

        var query = Db.Catch(
            fail<int>("Expected error"),
            err => err.Message.Contains("Expected"),
            _ => pure(42)
        ).As();

        var result = await query.Run(env).RunAsync();
        result.Should().Be(42);
    }

    [Fact]
    public async Task Catch_WithNonMatchingPredicate_RethrowsError()
    {
        var env = _fixture.CreateDbEnv();
        var errorMessage = "Specific error";

        var query = Db.Catch(
            fail<int>(errorMessage),
            err => err.Message.Contains("Different"),
            _ => pure(42)
        ).As();

        var act = async () => await query.Run(env).RunAsync();

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Message.Should().Contain(errorMessage);
    }

    [Fact]
    public async Task Catch_WithDatabaseError_CanRecover()
    {
        var env = _fixture.CreateDbEnv();

        // Add first user
        await (
            from _ in add(new User { Name = "Exists", Email = "catchdb@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        // Try duplicate, catch and recover
        var env2 = _fixture.CreateDbEnv();
        var query = Db.Catch(
            from _ in add(new User { Name = "Duplicate", Email = "catchdb@test.com" })
            from __ in saveChanges
            select "inserted",
            _ => true,
            _ => pure("recovered")
        ).As();

        var result = await query.Run(env2).RunAsync();
        result.Should().Be("recovered");
    }

    // ==================== Error in Nested Operations ====================

    [Fact]
    public async Task NestedBind_ErrorInInner_PropagatesOutward()
    {
        var env = _fixture.CreateDbEnv();

        var inner = fail<int>("Inner error");
        var outer =
            from x in pure(10)
            from y in inner
            select x + y;

        var act = async () => await outer.Run(env).RunAsync();

        var exception = await act.Should().ThrowAsync<Exception>();
        exception.Which.Message.Should().Contain("Inner error");
    }

    [Fact]
    public async Task ErrorAfterSuccessfulOperation_StillPropagates()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from entry in add(new User { Name = "BeforeError", Email = "beforeerror@test.com" })
            from _ in saveChanges
            from __ in fail<Unit>("Error after save")
            select unit;

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Error after save*");

        // Note: Without transaction, the save is already committed
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "beforeerror@test.com");
        user.Should().NotBeNull(); // Was saved before error
    }

    // ==================== Error Type Preservation ====================

    [Fact]
    public async Task DbUpdateException_IsPreservedCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        // Create constraint violation
        await (
            from _ in add(new User { Name = "First", Email = "preserve@test.com" })
            from __ in saveChanges
            select unit
        ).Run(env).RunAsync();

        var env2 = _fixture.CreateDbEnv();
        var query =
            from _ in add(new User { Name = "Duplicate", Email = "preserve@test.com" })
            from __ in saveChanges
            select unit;

        var act = async () => await query.Run(env2).RunAsync();

        // The specific exception type should be preserved
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
