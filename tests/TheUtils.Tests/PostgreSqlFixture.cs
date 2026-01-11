namespace TheUtils.Tests;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Shared PostgreSQL container fixture for all tests.
/// Implements IAsyncLifetime to handle container lifecycle.
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("testdb")
        .WithUsername("testuser")
        .WithPassword("testpass")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Create schema
        await using var context = CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Create a new DbContext connected to the test container.
    /// </summary>
    public TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new TestDbContext(options);
    }

    /// <summary>
    /// Create a PgEnv for the Pg monad.
    /// </summary>
    public PgEnv CreatePgEnv()
    {
        var context = CreateDbContext();
        return new PgEnv(context);
    }

    /// <summary>
    /// Create a PgEnv with a dedicated NpgsqlConnection for Npgsql-specific features.
    /// </summary>
    public PgEnv CreatePgEnvWithConnection()
    {
        var context = CreateDbContext();
        var connection = new NpgsqlConnection(ConnectionString);
        return new PgEnv(context, connection);
    }

    /// <summary>
    /// Reset the database to a clean state (truncate all tables).
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE users, accounts, documents RESTART IDENTITY CASCADE;
        ");
    }
}

/// <summary>
/// Collection definition for sharing the PostgreSQL fixture across test classes.
/// </summary>
[CollectionDefinition("PostgreSQL")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
}
