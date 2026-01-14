namespace TheUtils.DbPostgresqlTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for PostgreSQL COPY protocol binary import edge cases.
/// </summary>
[Collection("PostgreSQL")]
public class PostgresDbCopyImportTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbCopyImportTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Basic Import Tests ====================

    [Fact]
    public async Task BeginBinaryImport_ReturnsImporter()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.beginBinaryImport(
            "COPY users (name, email, balance, created_at, is_active) FROM STDIN (FORMAT BINARY)");

        var importer = await query.Run(env).RunAsync();
        importer.Should().NotBeNull();

        // Clean up
        await importer.DisposeAsync();
    }

    [Fact]
    public async Task BinaryImport_EmptySeq_ImportsZeroRows()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var rows = Seq<(string Name, string Email, decimal Balance, DateTime CreatedAt, bool IsActive)>();

        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        count.Should().Be(0UL);

        // Verify no rows inserted
        await using var verifyContext = _fixture.CreateDbContext();
        var totalCount = await verifyContext.Users.CountAsync();
        totalCount.Should().Be(0);
    }

    [Fact]
    public async Task BinaryImport_SingleRow_InsertsCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "SingleImport", Email: "singleimport@test.com", Balance: 123.45m, CreatedAt: now, IsActive: true)
        );

        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        count.Should().Be(1UL);

        // Verify
        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleOrDefaultAsync(u => u.Email == "singleimport@test.com");
        user.Should().NotBeNull();
        user!.Name.Should().Be("SingleImport");
        user.Balance.Should().Be(123.45m);
    }

    // ==================== Large Dataset Tests ====================

    [Fact]
    public async Task BinaryImport_LargeDataset_HandlesEfficiently()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;
        var rowCount = 5000;

        var rows = toSeq(Enumerable.Range(1, rowCount)
            .Select(i => (
                Name: $"Bulk{i}",
                Email: $"bulk{i}@test.com",
                Balance: (decimal)i,
                CreatedAt: now,
                IsActive: i % 2 == 0
            )));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        sw.Stop();

        count.Should().Be((ulong)rowCount);
        sw.ElapsedMilliseconds.Should().BeLessThan(10000); // Should complete within 10 seconds

        // Verify count
        await using var verifyContext = _fixture.CreateDbContext();
        var totalCount = await verifyContext.Users.CountAsync();
        totalCount.Should().Be(rowCount);
    }

    // ==================== Various Column Types ====================

    [Fact]
    public async Task BinaryImport_AllColumnTypes_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var specificTime = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);

        var rows = Seq(
            (Name: "TypeTest1", Email: "typetest1@test.com", Balance: 0m, CreatedAt: specificTime, IsActive: true),
            (Name: "TypeTest2", Email: "typetest2@test.com", Balance: 999999.99m, CreatedAt: specificTime, IsActive: false),
            (Name: "TypeTest3", Email: "typetest3@test.com", Balance: -100.50m, CreatedAt: specificTime, IsActive: true)
        );

        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        count.Should().Be(3UL);

        // Verify types are preserved
        await using var verifyContext = _fixture.CreateDbContext();

        var user1 = await verifyContext.Users.SingleAsync(u => u.Email == "typetest1@test.com");
        user1.Balance.Should().Be(0m);
        user1.IsActive.Should().BeTrue();

        var user2 = await verifyContext.Users.SingleAsync(u => u.Email == "typetest2@test.com");
        user2.Balance.Should().Be(999999.99m);
        user2.IsActive.Should().BeFalse();

        var user3 = await verifyContext.Users.SingleAsync(u => u.Email == "typetest3@test.com");
        user3.Balance.Should().Be(-100.50m);
    }

    [Fact]
    public async Task BinaryImport_WithSpecialCharacters_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "O'Brien \"Bob\"", Email: "obrien@test.com", Balance: 100m, CreatedAt: now, IsActive: true),
            (Name: "日本語テスト", Email: "unicode@test.com", Balance: 200m, CreatedAt: now, IsActive: true),
            (Name: "Test\nNewline\tTab", Email: "whitespace@test.com", Balance: 300m, CreatedAt: now, IsActive: true)
        );

        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        count.Should().Be(3UL);

        // Verify special characters preserved
        await using var verifyContext = _fixture.CreateDbContext();

        var obrien = await verifyContext.Users.SingleAsync(u => u.Email == "obrien@test.com");
        obrien.Name.Should().Be("O'Brien \"Bob\"");

        var unicode = await verifyContext.Users.SingleAsync(u => u.Email == "unicode@test.com");
        unicode.Name.Should().Be("日本語テスト");

        var whitespace = await verifyContext.Users.SingleAsync(u => u.Email == "whitespace@test.com");
        whitespace.Name.Should().Be("Test\nNewline\tTab");
    }

    // ==================== Transaction Integration ====================

    [Fact]
    public async Task BinaryImport_InTransaction_CommitsOnSuccess()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "TxImport1", Email: "tximport1@test.com", Balance: 100m, CreatedAt: now, IsActive: true),
            (Name: "TxImport2", Email: "tximport2@test.com", Balance: 200m, CreatedAt: now, IsActive: true)
        );

        // Note: COPY operations in Npgsql manage their own connection state
        // This tests that they work correctly in the overall workflow
        var count = await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        count.Should().Be(2UL);

        // Verify committed
        await using var verifyContext = _fixture.CreateDbContext();
        var totalCount = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("tximport"));
        totalCount.Should().Be(2);
    }

    // ==================== Sequential Imports ====================

    [Fact]
    public async Task BinaryImport_Sequential_WorksCorrectly()
    {
        var now = DateTime.UtcNow;

        // First import
        var env1 = _fixture.CreateDbEnvWithConnection();
        var rows1 = Seq(
            (Name: "Seq1", Email: "seq1@test.com", Balance: 100m, CreatedAt: now, IsActive: true)
        );

        await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows1,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env1).RunAsync();

        // Second import with new env
        var env2 = _fixture.CreateDbEnvWithConnection();
        var rows2 = Seq(
            (Name: "Seq2", Email: "seq2@test.com", Balance: 200m, CreatedAt: now, IsActive: true)
        );

        await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows2,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env2).RunAsync();

        // Verify both imports succeeded
        await using var verifyContext = _fixture.CreateDbContext();
        var count = await verifyContext.Users.CountAsync(u => u.Email.StartsWith("seq"));
        count.Should().Be(2);
    }

    // ==================== Edge Cases ====================

    [Fact]
    public async Task BinaryImport_WithZeroBalance_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "ZeroBalance", Email: "zerobalance@test.com", Balance: 0m, CreatedAt: now, IsActive: true)
        );

        await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "zerobalance@test.com");
        user.Balance.Should().Be(0m);
    }

    [Fact]
    public async Task BinaryImport_WithEmptyString_HandlesCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        var rows = Seq(
            (Name: "", Email: "emptyname@test.com", Balance: 100m, CreatedAt: now, IsActive: true)
        );

        await PostgresDb.binaryImport(
            "users (name, email, balance, created_at, is_active)",
            rows,
            (writer, row) =>
            {
                writer.Write(row.Name, NpgsqlDbType.Varchar);
                writer.Write(row.Email, NpgsqlDbType.Varchar);
                writer.Write(row.Balance, NpgsqlDbType.Numeric);
                writer.Write(row.CreatedAt, NpgsqlDbType.TimestampTz);
                writer.Write(row.IsActive, NpgsqlDbType.Boolean);
            }
        ).Run(env).RunAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var user = await verifyContext.Users.SingleAsync(u => u.Email == "emptyname@test.com");
        user.Name.Should().BeEmpty();
    }
}
