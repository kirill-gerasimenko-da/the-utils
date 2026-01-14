namespace TheUtils.DbPostgresTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for Postgres COPY protocol export operations.
/// </summary>
[Collection("Postgres")]
public class PostgresDbCopyExportTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbCopyExportTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Binary Export Basic Tests ====================

    [Fact]
    public async Task BeginBinaryExport_ReturnsExporter()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // First insert some data
        await addRange(Seq(
            new User { Name = "Export1", Email = "export1@test.com", Balance = 100 },
            new User { Name = "Export2", Email = "export2@test.com", Balance = 200 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Get exporter
        var query = PostgresDb.beginBinaryExport("COPY users (name, email, balance) TO STDOUT (FORMAT BINARY)");

        var exporter = await query.Run(env).RunAsync();
        exporter.Should().NotBeNull();

        // Clean up
        await exporter.DisposeAsync();
    }

    [Fact]
    public async Task BinaryExport_ReadsData_ReturnsCorrectRows()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Insert test data
        var now = DateTime.UtcNow;
        await addRange(Seq(
            new User { Name = "BE1", Email = "be1@test.com", Balance = 100 },
            new User { Name = "BE2", Email = "be2@test.com", Balance = 200 },
            new User { Name = "BE3", Email = "be3@test.com", Balance = 300 }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // Export and read data
        var env2 = _fixture.CreateDbEnvWithConnection();
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name, email, balance FROM users WHERE email LIKE 'be%' ORDER BY name) TO STDOUT (FORMAT BINARY)")
            from results in Db.liftIO<List<(string Name, string Email, decimal Balance)>>(async io =>
            {
                var list = new List<(string, string, decimal)>();
                while (await exporter.StartRowAsync(io.Token) != -1)
                {
                    var name = await exporter.ReadAsync<string>(io.Token);
                    var email = await exporter.ReadAsync<string>(io.Token);
                    var balance = await exporter.ReadAsync<decimal>(io.Token);
                    list.Add((name, email, balance));
                }
                await exporter.DisposeAsync();
                return list;
            })
            select results;

        var result = await query.Run(env2).RunAsync();

        result.Count.Should().Be(3);
        result[0].Name.Should().Be("BE1");
        result[0].Balance.Should().Be(100);
        result[1].Name.Should().Be("BE2");
        result[1].Balance.Should().Be(200);
        result[2].Name.Should().Be("BE3");
        result[2].Balance.Should().Be(300);
    }

    [Fact]
    public async Task BinaryExport_EmptyTable_ReturnsZeroRows()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Export from empty table (after reset)
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name FROM users WHERE email LIKE 'nonexistent%') TO STDOUT (FORMAT BINARY)")
            from results in Db.liftIO<int>(async io =>
            {
                var count = 0;
                while (await exporter.StartRowAsync(io.Token) != -1)
                {
                    await exporter.ReadAsync<string>(io.Token);
                    count++;
                }
                await exporter.DisposeAsync();
                return count;
            })
            select results;

        var result = await query.Run(env).RunAsync();
        result.Should().Be(0);
    }

    [Fact]
    public async Task BinaryExport_LargeDataset_HandlesEfficiently()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var now = DateTime.UtcNow;

        // Insert 1000 rows using COPY protocol for speed
        var rows = toSeq(Enumerable.Range(1, 1000)
            .Select(i => (
                Name: $"Bulk{i}",
                Email: $"bulk{i}@test.com",
                Balance: (decimal)i,
                CreatedAt: now,
                IsActive: true
            )));

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

        // Export and count
        var env2 = _fixture.CreateDbEnvWithConnection();
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name FROM users WHERE email LIKE 'bulk%') TO STDOUT (FORMAT BINARY)")
            from count in Db.liftIO<int>(async io =>
            {
                var c = 0;
                while (await exporter.StartRowAsync(io.Token) != -1)
                {
                    await exporter.ReadAsync<string>(io.Token);
                    c++;
                }
                await exporter.DisposeAsync();
                return c;
            })
            select count;

        var result = await query.Run(env2).RunAsync();
        result.Should().Be(1000);
    }

    // ==================== Export with Different Column Types ====================

    [Fact]
    public async Task BinaryExport_WithNullableColumns_HandlesNulls()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        // Insert user with default (null-like) values
        await add(new User { Name = "NullTest", Email = "nulltest@test.com", Balance = 0 })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var env2 = _fixture.CreateDbEnvWithConnection();
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name, balance FROM users WHERE email = 'nulltest@test.com') TO STDOUT (FORMAT BINARY)")
            from results in Db.liftIO<(string Name, decimal Balance)>(async io =>
            {
                await exporter.StartRowAsync(io.Token);
                var name = await exporter.ReadAsync<string>(io.Token);
                var balance = await exporter.ReadAsync<decimal>(io.Token);
                await exporter.DisposeAsync();
                return (name, balance);
            })
            select results;

        var result = await query.Run(env2).RunAsync();
        result.Name.Should().Be("NullTest");
        result.Balance.Should().Be(0);
    }

    [Fact]
    public async Task BinaryExport_WithBooleanColumn_ReadsCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        await addRange(Seq(
            new User { Name = "Active", Email = "active@test.com", IsActive = true },
            new User { Name = "Inactive", Email = "inactive@test.com", IsActive = false }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        var env2 = _fixture.CreateDbEnvWithConnection();
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name, is_active FROM users WHERE email IN ('active@test.com', 'inactive@test.com') ORDER BY name) TO STDOUT (FORMAT BINARY)")
            from results in Db.liftIO<List<(string Name, bool IsActive)>>(async io =>
            {
                var list = new List<(string, bool)>();
                while (await exporter.StartRowAsync(io.Token) != -1)
                {
                    var name = await exporter.ReadAsync<string>(io.Token);
                    var isActive = await exporter.ReadAsync<bool>(io.Token);
                    list.Add((name, isActive));
                }
                await exporter.DisposeAsync();
                return list;
            })
            select results;

        var result = await query.Run(env2).RunAsync();
        result.Count.Should().Be(2);
        result.First(r => r.Name == "Active").IsActive.Should().BeTrue();
        result.First(r => r.Name == "Inactive").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task BinaryExport_WithTimestamp_ReadsCorrectly()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var specificTime = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        await add(new User { Name = "TimeTest", Email = "timetest@test.com", CreatedAt = specificTime })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var env2 = _fixture.CreateDbEnvWithConnection();
        var query =
            from exporter in PostgresDb.beginBinaryExport(
                "COPY (SELECT name, created_at FROM users WHERE email = 'timetest@test.com') TO STDOUT (FORMAT BINARY)")
            from results in Db.liftIO<(string Name, DateTime CreatedAt)>(async io =>
            {
                await exporter.StartRowAsync(io.Token);
                var name = await exporter.ReadAsync<string>(io.Token);
                var createdAt = await exporter.ReadAsync<DateTime>(io.Token);
                await exporter.DisposeAsync();
                return (name, createdAt);
            })
            select results;

        var result = await query.Run(env2).RunAsync();
        result.Name.Should().Be("TimeTest");
        result.CreatedAt.Should().BeCloseTo(specificTime, TimeSpan.FromSeconds(1));
    }
}
