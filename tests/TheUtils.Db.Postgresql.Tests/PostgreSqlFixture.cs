namespace TheUtils.DbPostgresqlTests;

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
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
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
    /// Create a DbEnv for the Db monad.
    /// </summary>
    public DbEnv CreateDbEnv()
    {
        var context = CreateDbContext();
        return new DbEnv(context);
    }

    /// <summary>
    /// Create a DbEnv with a dedicated NpgsqlConnection for PostgreSQL-specific features.
    /// </summary>
    public DbEnv CreateDbEnvWithConnection()
    {
        var context = CreateDbContext();
        var connection = new NpgsqlConnection(ConnectionString);
        return new DbEnv(context, connection);
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
