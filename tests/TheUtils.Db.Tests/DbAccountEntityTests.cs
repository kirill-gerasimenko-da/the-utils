namespace TheUtils.DbTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for Account entity operations - tests the second entity type to ensure
/// the Db monad works with multiple entity types.
/// </summary>
[Collection("PostgreSQL")]
public class DbAccountEntityTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DbAccountEntityTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== CRUD Operations ====================

    [Fact]
    public async Task Account_Add_InsertsCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        var account = new Account
        {
            UserId = 1,
            Balance = 1000m,
            Currency = "USD"
        };

        await add(account)
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.Set<Account>().SingleOrDefaultAsync(a => a.UserId == 1);
        saved.Should().NotBeNull();
        saved!.Balance.Should().Be(1000m);
        saved.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Account_AddRange_InsertsMultiple()
    {
        var env = _fixture.CreateDbEnv();

        var accounts = Seq(
            new Account { UserId = 1, Balance = 100m, Currency = "USD" },
            new Account { UserId = 2, Balance = 200m, Currency = "EUR" },
            new Account { UserId = 3, Balance = 300m, Currency = "GBP" }
        );

        await addRange(accounts)
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Set<Account>().CountAsync();
        count.Should().Be(3);
    }

    [Fact]
    public async Task Account_Query_FiltersByUserId()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new Account { UserId = 100, Balance = 500m },
            new Account { UserId = 100, Balance = 300m },
            new Account { UserId = 200, Balance = 1000m }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var results = await seq(env.Context.Set<Account>().Where(a => a.UserId == 100))
            .Run(env).RunAsync();

        results.Count.Should().Be(2);
        results.All(a => a.UserId == 100).Should().BeTrue();
    }

    [Fact]
    public async Task Account_Update_ModifiesBalance()
    {
        var env = _fixture.CreateDbEnv();

        // Create account
        var entry = await add(new Account { UserId = 50, Balance = 100m })
            .Bind(e => saveChanges.Map(_ => e))
            .Run(env).RunAsync();

        var accountId = entry.Entity.Id;

        // Update balance
        var account = await head(env.Context.Set<Account>().Where(a => a.Id == accountId))
            .Run(env).RunAsync();

        account.IsSome.Should().BeTrue();
        account.IfSome(a =>
        {
            a.Balance = 999m;
        });

        await saveChanges.Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var updated = await verifyContext.Set<Account>().SingleAsync(a => a.Id == accountId);
        updated.Balance.Should().Be(999m);
    }

    [Fact]
    public async Task Account_Delete_RemovesCorrectly()
    {
        var env = _fixture.CreateDbEnv();

        var entry = await add(new Account { UserId = 60, Balance = 500m })
            .Bind(e => saveChanges.Map(_ => e))
            .Run(env).RunAsync();

        var accountId = entry.Entity.Id;

        // Delete
        var account = await single(env.Context.Set<Account>().Where(a => a.Id == accountId))
            .Run(env).RunAsync();

        await delete(account)
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var exists = await verifyContext.Set<Account>().AnyAsync(a => a.Id == accountId);
        exists.Should().BeFalse();
    }

    // ==================== Multiple Currencies ====================

    [Fact]
    public async Task Account_GroupByCurrency_ReturnsCorrectGroups()
    {
        var env = _fixture.CreateDbEnv();

        await addRange(Seq(
            new Account { UserId = 1, Balance = 100m, Currency = "USD" },
            new Account { UserId = 2, Balance = 200m, Currency = "USD" },
            new Account { UserId = 3, Balance = 300m, Currency = "EUR" },
            new Account { UserId = 4, Balance = 400m, Currency = "EUR" },
            new Account { UserId = 5, Balance = 500m, Currency = "GBP" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var results = await seq(env.Context.Set<Account>()
            .GroupBy(a => a.Currency)
            .Select(g => new { Currency = g.Key, Total = g.Sum(a => a.Balance) }))
            .Run(env).RunAsync();

        results.Count.Should().Be(3);
        results.Single(r => r.Currency == "USD").Total.Should().Be(300m);
        results.Single(r => r.Currency == "EUR").Total.Should().Be(700m);
        results.Single(r => r.Currency == "GBP").Total.Should().Be(500m);
    }

    // ==================== Transaction Tests ====================

    [Fact]
    public async Task Account_TransferFunds_TransactSucceeds()
    {
        var env = _fixture.CreateDbEnv();

        // Create two accounts
        await addRange(Seq(
            new Account { UserId = 1, Balance = 1000m, Currency = "USD" },
            new Account { UserId = 2, Balance = 500m, Currency = "USD" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Transfer 200 from user 1 to user 2 in a transaction
        var query = transact(
            from accounts in seq(env.Context.Set<Account>().Where(a => a.UserId == 1 || a.UserId == 2))
            let fromAccount = accounts.Single(a => a.UserId == 1)
            let toAccount = accounts.Single(a => a.UserId == 2)
            from _ in Db.liftIO(() =>
            {
                fromAccount.Balance -= 200m;
                toAccount.Balance += 200m;
                return unit;
            })
            from __ in saveChanges
            select unit
        );

        await query.Run(env).RunAsync();

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var account1 = await verifyContext.Set<Account>().SingleAsync(a => a.UserId == 1);
        var account2 = await verifyContext.Set<Account>().SingleAsync(a => a.UserId == 2);

        account1.Balance.Should().Be(800m);
        account2.Balance.Should().Be(700m);
    }

    [Fact]
    public async Task Account_TransferFunds_RollsBackOnError()
    {
        var env = _fixture.CreateDbEnv();

        // Create account with 500 balance
        await add(new Account { UserId = 10, Balance = 500m, Currency = "USD" })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Try to transfer more than available (should fail and rollback)
        var query = transact(
            from account in single(env.Context.Set<Account>().Where(a => a.UserId == 10))
            from _ in Db.liftIO(() =>
            {
                if (account.Balance < 1000m)
                    throw new InvalidOperationException("Insufficient funds");
                account.Balance -= 1000m;
                return unit;
            })
            from __ in saveChanges
            select unit
        );

        var act = async () => await query.Run(env).RunAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Balance should be unchanged
        await using var verifyContext = _fixture.CreateDbContext();
        var verifyAccount = await verifyContext.Set<Account>().SingleAsync(a => a.UserId == 10);
        verifyAccount.Balance.Should().Be(500m);
    }
}
