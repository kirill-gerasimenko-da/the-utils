namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for monad trait extensions and DbRT factory methods.
/// </summary>
[Collection("PostgreSQL")]
public class DbTraitExtensionsTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbTraitExtensionsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== DbRT Factory Methods ====================

    [Fact]
    public void DbRT_FromContext_CreatesEnv()
    {
        var context = _fixture.CreateDbContext();
        var env = DbRT.FromContext(context);

        env.Should().NotBeNull();
        env.Context.Should().Be(context);
        env.DefaultIsolation.IsNone.Should().BeTrue();
        env.CommandTimeout.IsNone.Should().BeTrue();
    }

    // ==================== Ignore Extension ====================

    [Fact]
    public async Task Ignore_DiscardsResult_ReturnsUnit()
    {
        var env = _fixture.CreateDbRT();

        // Create an operation that returns a value
        var operation = add(new User { Name = "IgnoreTest", Email = "ignoretest@test.com" });

        var ignored = operation.Map(_ => unit);

        var result = await ignored.RunIO(env).RunAsync();

        result.Should().Be(unit);
    }

    [Fact]
    public async Task Ignore_StillExecutesOperation()
    {
        var env = _fixture.CreateDbRT();

        // Add a user and ignore the result using Map to unit
        await add(new User { Name = "IgnoreExec", Email = "ignoreexec@test.com" })
            .Map(_ => unit)
            .RunIO(env).RunAsync();

        await saveChanges.RunIO(env).RunAsync();

        // Verify the user was actually added
        var count = await Db.count(env.Context.Set<User>().Where(u => u.Email == "ignoreexec@test.com"))
            .RunIO(env).RunAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task Ignore_ChainedOperations_WorkCorrectly()
    {
        var env = _fixture.CreateDbRT();

        var query =
            from _ in add(new User { Name = "Chain1", Email = "chain1@test.com" }).Map(_ => unit)
            from __ in add(new User { Name = "Chain2", Email = "chain2@test.com" }).Map(_ => unit)
            from ___ in saveChanges.Map(_ => unit)
            select unit;

        await query.RunIO(env).RunAsync();

        var count = await Db.count(env.Context.Set<User>().Where(u => u.Email.StartsWith("chain")))
            .RunIO(env).RunAsync();

        count.Should().Be(2);
    }

    // ==================== Environment Access ====================

    [Fact]
    public async Task Env_ReadsEnvironment()
    {
        var context = _fixture.CreateDbContext();
        var isolation = System.Data.IsolationLevel.Serializable;
        var env = new DbRT(context, DefaultIsolation: isolation);

        var result = await Db.runtime.RunIO(env).RunAsync();

        result.DefaultIsolation.IsSome.Should().BeTrue();
        result.DefaultIsolation.IfSome(i => i.Should().Be(isolation));
    }

    // ==================== MonadIO Trait ====================

    [Fact]
    public async Task LiftIO_FromIO_LiftCorrectly()
    {
        var env = _fixture.CreateDbRT();

        // Create an IO operation
        var ioOperation = IO.lift(() => 42);

        // Lift it into Db
        var dbOperation = Db.liftIO(ioOperation);

        var result = await dbOperation.RunIO(env).RunAsync();
        result.Should().Be(42);
    }

    // ==================== Fallible Trait ====================

    [Fact]
    public async Task Catch_RecoverFromError_ReturnsRecoveryValue()
    {
        var env = _fixture.CreateDbRT();

        var operation = fail<int>("Test error");

        var recovered = operation
            .Catch(_ => true, _ => pure(999));

        var result = await recovered.RunIO(env).RunAsync();
        result.Should().Be(999);
    }

    [Fact]
    public async Task Catch_WithPredicate_OnlyMatchingErrors()
    {
        var env = _fixture.CreateDbRT();

        var operation = fail<int>("Specific error");

        var recovered = operation
            .Catch(err => err.Message.Contains("Specific"), _ => pure(123));

        var result = await recovered.RunIO(env).RunAsync();
        result.Should().Be(123);
    }

}
