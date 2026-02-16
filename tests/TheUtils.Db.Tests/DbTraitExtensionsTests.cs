namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for monad trait extensions and DbEnv factory methods.
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

    // ==================== DbEnv Factory Methods ====================

    [Fact]
    public void DbEnv_FromContext_CreatesEnv()
    {
        var context = _fixture.CreateDbContext();
        var env = DbEnv.FromContext(context);

        env.Should().NotBeNull();
        env.Context.Should().Be(context);
        env.DefaultIsolation.IsNone.Should().BeTrue();
        env.CommandTimeout.IsNone.Should().BeTrue();
    }

    // ==================== Ignore Extension ====================

    [Fact]
    public async Task Ignore_DiscardsResult_ReturnsUnit()
    {
        var env = _fixture.CreateDbEnv();

        // Create an operation that returns a value
        var operation = add(new User { Name = "IgnoreTest", Email = "ignoretest@test.com" });

        // Using DbExtensions.Ignore to avoid conflict with LanguageExt.Prelude.Ignore
        var ignored = DbExtensions.Ignore(operation);

        var result = await ignored.Run(env).RunAsync();

        result.Should().Be(unit);
    }

    [Fact]
    public async Task Ignore_StillExecutesOperation()
    {
        var env = _fixture.CreateDbEnv();

        // Add a user and ignore the result using Map to unit
        await add(new User { Name = "IgnoreExec", Email = "ignoreexec@test.com" })
            .Map(_ => unit)
            .Run(env).RunAsync();

        await saveChanges.Run(env).RunAsync();

        // Verify the user was actually added
        var count = await Db.count(env.Context.Set<User>().Where(u => u.Email == "ignoreexec@test.com"))
            .Run(env).RunAsync();

        count.Should().Be(1);
    }

    [Fact]
    public async Task Ignore_ChainedOperations_WorkCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        var query =
            from _ in add(new User { Name = "Chain1", Email = "chain1@test.com" }).Map(_ => unit)
            from __ in add(new User { Name = "Chain2", Email = "chain2@test.com" }).Map(_ => unit)
            from ___ in saveChanges.Map(_ => unit)
            select unit;

        await query.Run(env).RunAsync();

        var count = await Db.count(env.Context.Set<User>().Where(u => u.Email.StartsWith("chain")))
            .Run(env).RunAsync();

        count.Should().Be(2);
    }

    // ==================== As Extension ====================

    [Fact]
    public async Task As_ConvertsKToDb()
    {
        var env = _fixture.CreateDbEnv();

        // The As extension converts K<Db, A> to Db<A>
        K<Db, int> kValue = pure(42);
        Db<int> dbValue = kValue.As();

        var result = await dbValue.Run(env).RunAsync();
        result.Should().Be(42);
    }

    // ==================== Environment Access ====================

    [Fact]
    public async Task Env_ReadsEnvironment()
    {
        var context = _fixture.CreateDbContext();
        var isolation = System.Data.IsolationLevel.Serializable;
        var env = new DbEnv(context, DefaultIsolation: isolation);

        var result = await Db.env.Run(env).RunAsync();

        result.DefaultIsolation.IsSome.Should().BeTrue();
        result.DefaultIsolation.IfSome(i => i.Should().Be(isolation));
    }

    // ==================== MonadIO Trait ====================

    [Fact]
    public async Task LiftIO_FromIO_LiftCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        // Create an IO operation
        var ioOperation = IO.lift(() => 42);

        // Lift it into Db
        var dbOperation = Db.liftIO(ioOperation);

        var result = await dbOperation.Run(env).RunAsync();
        result.Should().Be(42);
    }

    // ==================== Fallible Trait ====================

    [Fact]
    public async Task Catch_RecoverFromError_ReturnsRecoveryValue()
    {
        var env = _fixture.CreateDbEnv();

        var operation = fail<int>("Test error");

        // Use Db.Catch explicitly to avoid extension method conflicts
        var recovered = Db.Catch(
            operation,
            _ => true,  // Match all errors
            err => pure(999)
        ).As();

        var result = await recovered.Run(env).RunAsync();
        result.Should().Be(999);
    }

    [Fact]
    public async Task Catch_WithPredicate_OnlyMatchingErrors()
    {
        var env = _fixture.CreateDbEnv();

        var operation = fail<int>("Specific error");

        // Catch only errors containing "Specific"
        var recovered = Db.Catch(
            operation,
            err => err.Message.Contains("Specific"),
            err => pure(123)
        ).As();

        var result = await recovered.Run(env).RunAsync();
        result.Should().Be(123);
    }

    [Fact]
    public async Task Catch_NonMatchingPredicate_RethrowsError()
    {
        var env = _fixture.CreateDbEnv();

        var operation = fail<int>("Different error");

        // Catch only errors containing "Specific"
        var recovered = Db.Catch(
            operation,
            err => err.Message.Contains("Specific"),
            err => pure(123)
        ).As();

        var act = async () => await recovered.Run(env).RunAsync();

        await act.Should().ThrowAsync<Exception>().WithMessage("*Different error*");
    }
}
