namespace TheUtils.Tests;

using FluentAssertions;
using LanguageExt;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Cli;

public class CliCompositionTests
{
    [Fact]
    public async Task Execute_WithLinqComposition_ChainsCommands()
    {
        // Arrange & Act
        var output = await (
            from result1 in executeToString("echo", ["step 1"])
            from result2 in executeToString("echo", ["step 2"])
            from result3 in executeToString("echo", ["step 3"])
            select (result1.Trim(), result2.Trim(), result3.Trim())
        ).RunAsync();

        // Assert
        output.Should().Be(("step 1", "step 2", "step 3"));
    }

    [Fact]
    public async Task Execute_WithMap_TransformsOutput()
    {
        // Arrange & Act
        var output = await executeToString("echo", ["hello"])
            .Map(s => s.Trim().ToUpper())
            .RunAsync();

        // Assert
        output.Should().Be("HELLO");
    }

    [Fact]
    public async Task Execute_WithBind_ChainsWithDependency()
    {
        // Arrange & Act
        var output = await executeToString("echo", ["5"])
            .Bind(num => executeToString("echo", [$"Number is {num.Trim()}"]))
            .RunAsync();

        // Assert
        output.Trim().Should().Be("Number is 5");
    }

    [Fact]
    public async Task Execute_WithTryCatch_HandlesErrors()
    {
        // Arrange & Act
        var result = await execute("false", validation: CliWrap.CommandResultValidation.None)
            .RunAsync();

        // Assert
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Execute_ErrorHandling_ThrowsOnFailure()
    {
        // Arrange & Act
        var act = async () => await execute("false")
            .RunAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExecuteString_Extension_ReturnsString()
    {
        // Arrange & Act
        var output = await "echo".executeString("hello world")
            .RunAsync();

        // Assert
        output.Trim().Should().Be("hello world");
    }

    [Fact]
    public async Task Execute_MultipleCommandsInSequence_RunsAll()
    {
        // Arrange & Act
        var outputs = await (
            from out1 in executeToString("echo", ["first"])
            from out2 in executeToString("echo", ["second"])
            from out3 in executeToString("echo", ["third"])
            select Seq(out1.Trim(), out2.Trim(), out3.Trim())
        ).RunAsync();

        // Assert
        outputs.ToArray().Should().BeEquivalentTo(["first", "second", "third"]);
    }

    [Fact]
    public async Task Execute_ConditionalLogic_WorksWithParsing()
    {
        // Arrange & Act
        var output = await (
            from num in executeToString("echo", ["10"])
            let parsed = int.Parse(num.Trim())
            from result in parsed > 5
                ? executeToString("echo", [$"{parsed} is greater than 5"])
                : executeToString("echo", ["too small"])
            select result.Trim()
        ).RunAsync();

        // Assert
        output.Should().Be("10 is greater than 5");
    }

    [Fact]
    public async Task Execute_WithIOOperations_MixesIOAndCli()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            var result = await (
                from _ in IO.lift(() => File.WriteAllText(tempFile, "test content"))
                from output in executeToString("cat", [tempFile])
                from __ in IO.lift(() => File.Delete(tempFile))
                select output.Trim()
            ).RunAsync();

            // Assert
            result.Should().Be("test content");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
