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
        var conn = _fixture.CreateConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "Test", Email: "test@test.com", Balance: 10m, CreatedAt: now, IsActive: true)
        );

        var query = PostgresDb.binaryImport(
            conn,
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

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task BinaryImport_InvalidColumnName_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "Test", Email: "test@test.com")
        );

        var query = PostgresDb.binaryImport(
            conn,
            "users (name, nonexistent_column)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
            }
        );

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task BinaryImport_TypeMismatch_ThrowsException()
    {
        var conn = _fixture.CreateConnection();

        var rows = Seq(
            (Name: "Test", Email: "test@test.com", Balance: "not_a_number")
        );

        var query = PostgresDb.binaryImport(
            conn,
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

        var act = async () => await query.RunAsync();

        // Should throw some kind of exception (either Postgres or InvalidCast)
        await act.Should().ThrowAsync<Exception>();
    }

    // ==================== Raw Query Errors ====================

    [Fact]
    public async Task RawQuery_InvalidSql_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.rawQuery<string>(
            conn,
            "SELECT * FROM INVALID SQL SYNTAX",
            reader => reader.GetString(0)
        );

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task RawQuery_NonexistentTable_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.rawQuery<string>(
            conn,
            "SELECT name FROM nonexistent_table",
            reader => reader.GetString(0)
        );

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task RawScalar_InvalidSql_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.rawScalar<long>(conn, "SELECT COUNT(*) FROM nonexistent");

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ==================== JSONB Errors ====================

    [Fact]
    public async Task JsonbPath_InvalidPath_HandlesGracefully()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        // Insert valid document
        await add(new Document
            {
                Title = "Test",
                Metadata = """{"valid": "json"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Query with unusual path - should return None if not found
        var query = PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.nonexistent.deep.path");

        var result = await query.RunAsync();
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_InvalidTable_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.jsonbPath<string>(conn, "nonexistent_table", "metadata", "$.field");

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    // ==================== Advisory Lock Edge Cases ====================

    [Fact]
    public async Task WithAdvisoryLock_OperationFails_ReleasesLock()
    {
        var conn1 = _fixture.CreateConnection();
        var conn2 = _fixture.CreateConnection();
        var lockKey = 999888L;

        // Execute operation that fails while holding lock
        var failingQuery = PostgresDb.withAdvisoryLock(conn1, lockKey,
            IO.fail<int>("Intentional failure")
        );

        var act = async () => await failingQuery.RunAsync();
        await act.Should().ThrowAsync<Exception>();

        // Lock should be released - another connection can acquire it
        var acquired = await PostgresDb.tryAdvisoryLock(conn2, lockKey).RunAsync();
        acquired.Should().BeTrue();

        // Cleanup
        await PostgresDb.advisoryUnlock(conn2, lockKey).RunAsync();
    }

    [Fact]
    public async Task AdvisoryLock_InvalidKey_DoesNotThrow()
    {
        var conn = _fixture.CreateConnection();

        // Negative keys are valid in PostgreSQL
        var lockKey = -12345L;

        var acquired = await PostgresDb.tryAdvisoryLock(conn, lockKey).RunAsync();
        acquired.Should().BeTrue();

        await PostgresDb.advisoryUnlock(conn, lockKey).RunAsync();
    }

    // ==================== LISTEN/NOTIFY Errors ====================

    [Fact]
    public async Task Listen_InvalidChannelName_ThrowsPostgresException()
    {
        var conn = _fixture.CreateConnection();

        // Channel names with special characters should fail
        var query = PostgresDb.listen(conn, "invalid channel; DROP TABLE users;");

        var act = async () => await query.RunAsync();

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Notify_WithSpecialCharactersInPayload_EscapesCorrectly()
    {
        var conn = _fixture.CreateConnection();

        // This should not throw - payload escaping handles quotes
        var query = PostgresDb.notify(conn, "test_channel", "payload with 'quotes' and special chars");

        await query.RunAsync();

        // If we got here, the payload was escaped correctly
    }
}
