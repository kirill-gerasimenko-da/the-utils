namespace TheUtils.DbPostgresTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for error scenarios in Postgres-specific operations.
/// </summary>
[Collection("Postgres")]
public class PostgresDbErrorTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbErrorTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== COPY Protocol Errors ====================

    [Fact]
    public async Task BinaryImport_InvalidTableName_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "Test", Email: "test@test.com", Balance: 10m, CreatedAt: now, IsActive: true)
        );

        var query = PostgresDb.binaryImport(
            "nonexistent_table (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task BinaryImport_InvalidColumnName_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "Test", Email: "test@test.com")
        );

        var query = PostgresDb.binaryImport(
            "users (name, nonexistent_column)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
            }
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task BinaryImport_TypeMismatch_ThrowsException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var rows = Seq(
            (Name: "Test", Email: "test@test.com", Balance: "not_a_number")
        );

        var query = PostgresDb.binaryImport(
            "users (name, email, balance)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                // Writing string as Numeric will fail
                writer.Write(row.Balance, NpgsqlDbType.Varchar); // Wrong type for balance column
            }
        );

        var act = async () => await query.Run(env).RunAsync();

        // Should throw some kind of exception (either Postgres or InvalidCast)
        await act.Should().ThrowAsync<Exception>();
    }

    // ==================== Raw Query Errors ====================

    [Fact]
    public async Task RawQuery_InvalidSql_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.rawQuery<string>(
            "SELECT * FROM INVALID SQL SYNTAX",
            reader => reader.GetString(0)
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task RawQuery_NonexistentTable_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.rawQuery<string>(
            "SELECT name FROM nonexistent_table",
            reader => reader.GetString(0)
        );

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task RawScalar_InvalidSql_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.rawScalar<long>("SELECT COUNT(*) FROM nonexistent");

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ==================== JSONB Errors ====================

    [Fact]
    public async Task JsonbPath_InvalidPath_HandlesGracefully()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Insert valid document
        await add(new Document
            {
                Title = "Test",
                Metadata = """{"valid": "json"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Query with unusual path - should return None if not found
        var query = PostgresDb.jsonbPath<string>("documents", "metadata", "$.nonexistent.deep.path");

        var result = await query.Run(env).RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_InvalidTable_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.jsonbPath<string>("nonexistent_table", "metadata", "$.field");

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ==================== Advisory Lock Edge Cases ====================

    [Fact]
    public async Task WithAdvisoryLock_OperationFails_ReleasesLock()
    {
        var env1 = _fixture.CreateDbEnvWithConnection();
        var env2 = _fixture.CreateDbEnvWithConnection();
        var lockKey = 999888L;

        // Execute operation that fails while holding lock
        var failingQuery = PostgresDb.withAdvisoryLock(lockKey,
            fail<int>("Intentional failure")
        );

        var act = async () => await failingQuery.Run(env1).RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Lock should be released - another connection can acquire it
        var acquired = await PostgresDb.tryAdvisoryLock(lockKey).Run(env2).RunAsync();
        acquired.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(lockKey).Run(env2).RunAsync();
    }

    [Fact]
    public async Task AdvisoryLock_InvalidKey_DoesNotThrow()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Negative keys are valid in PostgreSQL
        var lockKey = -12345L;

        var acquired = await PostgresDb.tryAdvisoryLock(lockKey).Run(env).RunAsync();
        acquired.Should().BeTrue();

        await PostgresDb.advisoryUnlock(lockKey).Run(env).RunAsync();
    }

    // ==================== LISTEN/NOTIFY Errors ====================

    [Fact]
    public async Task Listen_InvalidChannelName_ThrowsPostgresException()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Channel names with special characters should fail
        var query = PostgresDb.listen("invalid channel; DROP TABLE users;");

        var act = async () => await query.Run(env).RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Notify_WithSpecialCharactersInPayload_EscapesCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // This should not throw - payload escaping handles quotes
        var query = PostgresDb.notify("test_channel", "payload with 'quotes' and special chars");

        await query.Run(env).RunAsync();

        // If we got here, the payload was escaped correctly
    }
}
