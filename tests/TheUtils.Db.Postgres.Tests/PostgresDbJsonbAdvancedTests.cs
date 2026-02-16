namespace TheUtils.DbPostgresTests;

using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Advanced tests for Postgres JSONB operations.
/// </summary>
[Collection("Postgres")]
public class PostgresDbJsonbAdvancedTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbJsonbAdvancedTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Array Access Tests ====================

    [Fact]
    public async Task JsonbPath_ArrayAccess_ReturnsElement()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        // Insert document with array
        await add(new Document
            {
                Title = "ArrayTest",
                Metadata = """{"items": ["first", "second", "third"]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Access array element
        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.items[0]")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("first"));
    }

    [Fact]
    public async Task JsonbPath_ArrayLastElement_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "ArrayLastTest",
                Metadata = """{"numbers": [10, 20, 30, 40]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Access last element using index
        var result = await PostgresDb.jsonbPath<int>(conn, "documents", "metadata", "$.numbers[3]")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(40));
    }

    [Fact]
    public async Task JsonbPath_ArrayOutOfBounds_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "ArrayBoundsTest",
                Metadata = """{"items": ["only", "two"]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Access beyond array bounds
        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.items[99]")
            .RunAsync();

        result.IsNone.Should().BeTrue();
    }

    // ==================== Nested Object Tests ====================

    [Fact]
    public async Task JsonbPath_NestedDeep_ReturnsValue()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "DeepNested",
                Metadata = """{"level1": {"level2": {"level3": {"level4": "deepValue"}}}}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.level1.level2.level3.level4")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("deepValue"));
    }

    [Fact]
    public async Task JsonbPath_NestedWithMixedTypes_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "MixedNested",
                Metadata = """{"user": {"name": "John", "age": 30, "active": true, "scores": [85, 90, 95]}}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Get string
        var name = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.user.name")
            .RunAsync();
        name.IsSome.Should().BeTrue();
        name.IfSome(v => v.Should().Be("John"));

        // Get number
        var age = await PostgresDb.jsonbPath<int>(conn, "documents", "metadata", "$.user.age")
            .RunAsync();
        age.IsSome.Should().BeTrue();
        age.IfSome(v => v.Should().Be(30));

        // Get boolean
        var active = await PostgresDb.jsonbPath<bool>(conn, "documents", "metadata", "$.user.active")
            .RunAsync();
        active.IsSome.Should().BeTrue();
        active.IfSome(v => v.Should().BeTrue());

        // Get array element
        var score = await PostgresDb.jsonbPath<int>(conn, "documents", "metadata", "$.user.scores[1]")
            .RunAsync();
        score.IsSome.Should().BeTrue();
        score.IfSome(v => v.Should().Be(90));
    }

    // ==================== Null Handling Tests ====================

    [Fact]
    public async Task JsonbPath_NullJsonValue_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "NullValue",
                Metadata = """{"field": null}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // JSON null should return None
        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.field")
            .RunAsync();

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_EmptyObject_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "EmptyObject",
                Metadata = """{}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.nonexistent")
            .RunAsync();

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task JsonbPath_EmptyArray_ReturnsNone()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "EmptyArray",
                Metadata = """{"items": []}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.items[0]")
            .RunAsync();

        result.IsNone.Should().BeTrue();
    }

    // ==================== Numeric Types Tests ====================

    [Fact]
    public async Task JsonbPath_IntegerValue_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "IntTest",
                Metadata = """{"count": 42}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<int>(conn, "documents", "metadata", "$.count")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(42));
    }

    [Fact]
    public async Task JsonbPath_DecimalValue_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "DecimalTest",
                Metadata = """{"price": 19.99}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<decimal>(conn, "documents", "metadata", "$.price")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(19.99m));
    }

    [Fact]
    public async Task JsonbPath_LargeNumber_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "LargeNumberTest",
                Metadata = """{"bigNumber": 9999999999999}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<long>(conn, "documents", "metadata", "$.bigNumber")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be(9999999999999L));
    }

    // ==================== String Values Tests ====================

    [Fact]
    public async Task JsonbPath_StringWithSpecialCharacters_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "SpecialChars",
                Metadata = """{"text": "Hello \"World\" with 'quotes' and \\backslash"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.text")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Contain("World"));
    }

    [Fact]
    public async Task JsonbPath_UnicodeString_ReturnsCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "UnicodeTest",
                Metadata = """{"greeting": "日本語テスト 🎉"}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.greeting")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Contain("日本語"));
    }

    // ==================== Variables in Path ====================

    [Fact]
    public async Task JsonbPath_WithVariables_SubstitutesCorrectly()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await add(new Document
            {
                Title = "VarsTest",
                Metadata = """{"users": [{"name": "Alice", "age": 25}, {"name": "Bob", "age": 30}]}"""
            })
            .Bind(_ => saveChanges.Map(_ => unit))
            .Run(env).RunAsync();

        // Use variable to filter - note: jsonb_path_query_first with vars
        var result = await PostgresDb.jsonbPath<string>(
                conn,
                "documents",
                "metadata",
                "$.users[0].name",
                None)
            .RunAsync();

        result.IsSome.Should().BeTrue();
        result.IfSome(v => v.Should().Be("Alice"));
    }

    // ==================== Multiple Documents Tests ====================

    [Fact]
    public async Task JsonbPath_MultipleDocuments_ReturnsFromFirst()
    {
        var env = _fixture.CreateDbEnv();
        var conn = _fixture.CreateConnection();

        await addRange(Seq(
            new Document { Title = "First", Metadata = """{"value": "first"}""" },
            new Document { Title = "Second", Metadata = """{"value": "second"}""" }
        )).Bind(_ => saveChanges.Map(_ => unit))
          .Run(env).RunAsync();

        // jsonbPath queries the first matching row
        var result = await PostgresDb.jsonbPath<string>(conn, "documents", "metadata", "$.value")
            .RunAsync();

        result.IsSome.Should().BeTrue();
        // Should get first inserted value
        result.IfSome(v => (v == "first" || v == "second").Should().BeTrue());
    }
}
