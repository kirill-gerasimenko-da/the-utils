namespace TheUtils.Tests;

using FluentAssertions;
using LanguageExt;
using LanguageExt.Streaming;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Cli;
using CliWrap.EventStream;

public class CliSourceTTests
{
    [Fact]
    public async Task ExecuteSourceT_BasicCommand_YieldsEvents()
    {
        // Arrange
        var events = new List<CommandEvent>();

        // Act
        var sourceT = executeSourceT("echo", ["hello"]);

        // Reduce the stream into a list
        await sourceT
            .Fold(events, (list, evt) =>
            {
                list.Add(evt);
                return list;
            })
            .RunAsync();

        // Assert
        events.Should().ContainSingle(e => e is StartedCommandEvent);
        events.Should().ContainSingle(e => e is StandardOutputCommandEvent);
        events.Should().ContainSingle(e => e is ExitedCommandEvent);

        var outputEvent = events.OfType<StandardOutputCommandEvent>().Single();
        outputEvent.Text.Trim().Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteSourceT_CanComposeWithMap()
    {
        // Arrange & Act
        var outputs = new List<string>();

        var sourceT = executeSourceT("echo", ["test"])
            .Map(evt => evt switch
            {
                StandardOutputCommandEvent output => output.Text,
                _ => ""
            })
            .Filter(s => !string.IsNullOrWhiteSpace(s));

        await sourceT
            .Fold(outputs, (list, text) =>
            {
                list.Add(text);
                return list;
            })
            .RunAsync();

        // Assert
        outputs.Should().ContainSingle();
        outputs[0].Trim().Should().Be("test");
    }

    [Fact]
    public async Task ExecuteSourceT_WithStderr_YieldsErrorEvents()
    {
        // Arrange
        var events = new List<CommandEvent>();

        // Act
        var sourceT = executeSourceT(
            "sh",
            ["-c", "echo stdout; echo stderr >&2"]
        );

        await sourceT
            .Fold(events, (list, evt) =>
            {
                list.Add(evt);
                return list;
            })
            .RunAsync();

        // Assert
        events.Should().Contain(e => e is StandardOutputCommandEvent);
        events.Should().Contain(e => e is StandardErrorCommandEvent);

        var outputEvent = events.OfType<StandardOutputCommandEvent>().Single();
        outputEvent.Text.Trim().Should().Be("stdout");

        var errorEvent = events.OfType<StandardErrorCommandEvent>().Single();
        errorEvent.Text.Trim().Should().Be("stderr");
    }

    [Fact]
    public async Task ExecuteSourceT_CanLiftIntoOtherMonads()
    {
        // Arrange & Act - compose SourceT with IO operations
        var result = await (
            from _ in IO.lift(() => Console.WriteLine("Starting command..."))
            from sourceT in IO.pure(executeSourceT("echo", ["compose"]))
            from events in sourceT.Fold(
                List<CommandEvent>(),
                (list, evt) => list.Add(evt)
            )
            select events.OfType<StandardOutputCommandEvent>().Single().Text.Trim()
        ).RunAsync();

        // Assert
        result.Should().Be("compose");
    }

    [Fact]
    public async Task ExecuteSourceT_CountEvents_UsingFold()
    {
        // Arrange & Act
        var count = await executeSourceT("echo", ["count me"])
            .Fold(0, (acc, _) => acc + 1)
            .RunAsync();

        // Assert
        count.Should().BeGreaterThan(0);
        count.Should().Be(3); // Started + Output + Exited
    }

    [Fact]
    public async Task ExecuteSourceT_TakeFirstN_Events()
    {
        // Arrange & Act
        var events = new List<CommandEvent>();

        await executeSourceT("echo", ["take test"])
            .Take(2) // Take only first 2 events
            .Fold(events, (list, evt) =>
            {
                list.Add(evt);
                return list;
            })
            .RunAsync();

        // Assert
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<StartedCommandEvent>();
    }

    [Fact]
    public async Task ExecuteSourceT_FilterEvents_ByType()
    {
        // Arrange & Act
        var outputEvents = new List<StandardOutputCommandEvent>();

        await executeSourceT("echo", ["filter"])
            .Filter(evt => evt is StandardOutputCommandEvent)
            .Map(evt => (StandardOutputCommandEvent)evt)
            .Fold(outputEvents, (list, evt) =>
            {
                list.Add(evt);
                return list;
            })
            .RunAsync();

        // Assert
        outputEvents.Should().ContainSingle();
        outputEvents[0].Text.Trim().Should().Be("filter");
    }
}
