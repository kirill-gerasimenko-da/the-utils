namespace TheUtils.Tests;

using System.Text;
using CliWrap;
using CliWrap.EventStream;
using FluentAssertions;
using Xunit;
using static TheUtils.Cli;

public class CliAdvancedTests
{
    [Fact]
    public async Task ExecuteStream_YieldsAllEvents()
    {
        // Arrange
        var events = new List<CommandEvent>();

        // Act
        var streamIO = await executeStream("echo", ["hello"])
            .RunAsync();

        await foreach (var evt in streamIO)
        {
            events.Add(evt);
        }

        // Assert
        events.Should().ContainSingle(e => e is StartedCommandEvent);
        events.Should().ContainSingle(e => e is StandardOutputCommandEvent);
        events.Should().ContainSingle(e => e is ExitedCommandEvent);

        var outputEvent = events.OfType<StandardOutputCommandEvent>().Single();
        outputEvent.Text.Trim().Should().Be("hello");

        var exitEvent = events.OfType<ExitedCommandEvent>().Single();
        exitEvent.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteStream_WithStderr_YieldsErrorEvents()
    {
        // Arrange
        var events = new List<CommandEvent>();

        // Act
        var streamIO = await executeStream(
                "sh",
                ["-c", "echo stdout; echo stderr >&2"]
            )
            .RunAsync();

        await foreach (var evt in streamIO)
        {
            events.Add(evt);
        }

        // Assert
        events.Should().Contain(e => e is StandardOutputCommandEvent);
        events.Should().Contain(e => e is StandardErrorCommandEvent);

        var outputEvent = events.OfType<StandardOutputCommandEvent>().Single();
        outputEvent.Text.Trim().Should().Be("stdout");

        var errorEvent = events.OfType<StandardErrorCommandEvent>().Single();
        errorEvent.Text.Trim().Should().Be("stderr");
    }

    [Fact]
    public async Task Execute_WithCancellation_CancelsCommand()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        var act = async () => await execute("sleep", ["10"])
            .RunAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteWithCancellation_GracefulThenForceful_CancelsCommand()
    {
        // Arrange
        using var gracefulCts = new CancellationTokenSource();
        using var forcefulCts = new CancellationTokenSource();

        gracefulCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        forcefulCts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        var act = async () => await executeWithCancellation(
                gracefulCts.Token,
                forcefulCts.Token,
                "sleep",
                ["10"]
            )
            .RunAsync();

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteBuffered_WithCustomEncoding_UsesEncoding()
    {
        // Arrange
        var encoding = Encoding.UTF8;

        // Act
        var result = await executeBuffered(
                "echo",
                ["hello"],
                stdOutEncoding: encoding
            )
            .RunAsync();

        // Assert
        result.StandardOutput.Trim().Should().Be("hello");
    }

    [Fact]
    public async Task Execute_WithPipeTargetToFile_WritesToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            await execute(
                    "echo",
                    ["hello file"],
                    standardOutput: PipeTarget.ToFile(tempFile)
                )
                .RunAsync();

            // Assert
            var content = await File.ReadAllTextAsync(tempFile);
            content.Trim().Should().Be("hello file");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Execute_WithPipeTargetToDelegate_InvokesDelegate()
    {
        // Arrange
        var lines = new List<string>();

        // Act
        await execute(
                "sh",
                ["-c", "echo line1; echo line2; echo line3"],
                standardOutput: PipeTarget.ToDelegate(line => lines.Add(line))
            )
            .RunAsync();

        // Assert
        lines.Should().HaveCount(3);
        lines[0].Should().Be("line1");
        lines[1].Should().Be("line2");
        lines[2].Should().Be("line3");
    }

    [Fact]
    public async Task Command_Helper_ReturnsConfiguredCommand()
    {
        // Arrange & Act
        var cmd = command(
            "echo",
            ["hello"],
            workingDirectory: "/tmp"
        );

        // Assert
        cmd.Should().NotBeNull();
        cmd.TargetFilePath.Should().Be("echo");
    }

    [Fact]
    public async Task ExecuteObservable_ReturnsObservable()
    {
        // Arrange
        var events = new List<CommandEvent>();
        var tcs = new TaskCompletionSource<bool>();

        // Act
        var observableIO = await executeObservable("echo", ["hello"])
            .RunAsync();

        // Subscribe and collect events - using a simple observer
        var observer = new TestObserver(
            onNext: evt => events.Add(evt),
            onCompleted: () => tcs.SetResult(true)
        );
        observableIO.Subscribe(observer);

        await tcs.Task;

        // Assert
        events.Should().ContainSingle(e => e is StartedCommandEvent);
        events.Should().ContainSingle(e => e is StandardOutputCommandEvent);
        events.Should().ContainSingle(e => e is ExitedCommandEvent);
    }

    private class TestObserver(Action<CommandEvent> onNext, Action onCompleted) : IObserver<CommandEvent>
    {
        public void OnNext(CommandEvent value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() => onCompleted();
    }
}
