namespace TheUtils.Tests;

using FluentAssertions;
using LanguageExt;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Cli;

public class CliBasicTests
{
    [Fact]
    public async Task Execute_EchoCommand_ReturnsSuccess()
    {
        // Arrange & Act
        var result = await execute("echo", ["hello"])
            .RunAsync();

        // Assert
        result.ExitCode.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteToString_EchoCommand_ReturnsStdout()
    {
        // Arrange & Act
        var output = await executeToString("echo", ["hello world"])
            .RunAsync();

        // Assert
        output.Trim().Should().Be("hello world");
    }

    [Fact]
    public async Task Execute_WithMultipleArguments_PassesAllArguments()
    {
        // Arrange & Act
        var output = await executeToString("echo", ["one", "two", "three"])
            .RunAsync();

        // Assert
        output.Trim().Should().Be("one two three");
    }

    [Fact]
    public async Task Execute_WithWorkingDirectory_UsesCorrectDirectory()
    {
        // Arrange
        var expectedPath = "/tmp";

        // Act
        var output = await executeToString(
                "pwd",
                workingDirectory: expectedPath
            )
            .RunAsync();

        // Assert
        output.Trim().Should().Be(expectedPath);
    }

    [Fact]
    public async Task Execute_WithEnvironmentVariables_SetsVariables()
    {
        // Arrange
        var envVars = Map(
            ("TEST_VAR_1", (string?)"value1"),
            ("TEST_VAR_2", (string?)"value2")
        );

        // Act - echo environment variable
        var output = await executeToString(
                "sh",
                ["-c", "echo $TEST_VAR_1"],
                environmentVariables: envVars
            )
            .RunAsync();

        // Assert
        output.Trim().Should().Be("value1");
    }

    [Fact]
    public async Task Execute_CommandNotFound_ThrowsException()
    {
        // Arrange & Act
        var act = async () => await execute("nonexistent-command-12345")
            .RunAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Execute_NonZeroExitCode_ThrowsExceptionByDefault()
    {
        // Arrange & Act
        var act = async () => await execute("false")
            .RunAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Execute_NonZeroExitCode_WithValidationNone_DoesNotThrow()
    {
        // Arrange & Act
        var result = await execute(
                "false",
                validation: CliWrap.CommandResultValidation.None
            )
            .RunAsync();

        // Assert
        result.ExitCode.Should().NotBe(0);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteBuffered_CapturesStdoutAndStderr()
    {
        // Arrange & Act
        var result = await executeBuffered(
                "sh",
                ["-c", "echo stdout; echo stderr >&2"]
            )
            .RunAsync();

        // Assert
        result.StandardOutput.Trim().Should().Be("stdout");
        result.StandardError.Trim().Should().Be("stderr");
    }

    [Fact]
    public async Task ExecuteToString_EmptyOutput_ReturnsEmptyString()
    {
        // Arrange & Act
        var output = await executeToString("true")
            .RunAsync();

        // Assert
        output.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WithStandardInput_ReceivesInput()
    {
        // Arrange
        var inputSource = CliWrap.PipeSource.FromString("hello from stdin");

        // Act
        var output = await executeToString(
                "cat",
                standardInput: inputSource
            )
            .RunAsync();

        // Assert
        output.Trim().Should().Be("hello from stdin");
    }
}
