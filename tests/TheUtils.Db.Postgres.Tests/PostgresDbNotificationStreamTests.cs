namespace TheUtils.DbPostgresTests;

using FluentAssertions;
using LanguageExt;
using Npgsql;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for Postgres LISTEN/NOTIFY notification streaming.
/// </summary>
[Collection("Postgres")]
public class PostgresDbNotificationStreamTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PostgresDbNotificationStreamTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ==================== Basic LISTEN/NOTIFY ====================

    [Fact]
    public async Task ListenAndNotify_ReceivesNotification()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "test_channel_basic";
        var receivedPayload = "";
        var received = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        // Set up listener
        await PostgresDb.listen(listenerConn, channel).RunAsync();

        // Add notification handler
        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
            {
                receivedPayload = args.Payload;
                received.TrySetResult(true);
            }
        };

        // Start waiting for notifications in background
        var waitTask = Task.Run(async () =>
        {
            try { await listenerConn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification
        await PostgresDb.notify(notifierConn, channel, "hello_world").RunAsync();

        // Wait for notification (with timeout)
        var completedInTime = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task;

        completedInTime.Should().BeTrue("Notification should be received within timeout");
        receivedPayload.Should().Be("hello_world");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await waitTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();
    }

    [Fact]
    public async Task Notifications_ReceivesMultipleMessages()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "test_channel_multi";
        var receivedMessages = new List<string>();
        var messagesReceived = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(listenerConn, channel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
            {
                receivedMessages.Add(args.Payload);
                if (receivedMessages.Count >= 3)
                    messagesReceived.TrySetResult(true);
            }
        };

        // Start listening in background
        var listenTask = Task.Run(async () =>
        {
            try
            {
                while (receivedMessages.Count < 3)
                    await listenerConn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send multiple notifications
        await PostgresDb.notify(notifierConn, channel, "message1").RunAsync();
        await PostgresDb.notify(notifierConn, channel, "message2").RunAsync();
        await PostgresDb.notify(notifierConn, channel, "message3").RunAsync();

        var completed = await Task.WhenAny(messagesReceived.Task, Task.Delay(5000)) == messagesReceived.Task;

        completed.Should().BeTrue();
        receivedMessages.Should().HaveCount(3);
        receivedMessages.Should().Contain("message1");
        receivedMessages.Should().Contain("message2");
        receivedMessages.Should().Contain("message3");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();
    }

    [Fact]
    public async Task Notifications_WithPayload_ParsesCorrectly()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "test_channel_payload";
        var receivedPayloads = new List<string>();
        var done = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(listenerConn, channel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
            {
                receivedPayloads.Add(args.Payload);
                if (receivedPayloads.Count >= 2)
                    done.TrySetResult(true);
            }
        };

        var listenTask = Task.Run(async () =>
        {
            try
            {
                while (receivedPayloads.Count < 2)
                    await listenerConn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send payloads with different content
        await PostgresDb.notify(notifierConn, channel, """{"type":"event","data":123}""").RunAsync();
        await PostgresDb.notify(notifierConn, channel, "simple_text_payload").RunAsync();

        await Task.WhenAny(done.Task, Task.Delay(5000));

        receivedPayloads.Should().Contain("""{"type":"event","data":123}""");
        receivedPayloads.Should().Contain("simple_text_payload");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();
    }

    [Fact]
    public async Task Notifications_DifferentChannels_OnlyReceivesSubscribed()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var subscribedChannel = "subscribed_channel";
        var otherChannel = "other_channel";
        var receivedFromSubscribed = new List<string>();
        var receivedFromOther = new List<string>();
        var done = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        // Only listen to one channel
        await PostgresDb.listen(listenerConn, subscribedChannel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == subscribedChannel)
                receivedFromSubscribed.Add(args.Payload);
            else if (args.Channel == otherChannel)
                receivedFromOther.Add(args.Payload);

            if (receivedFromSubscribed.Count >= 1)
                done.TrySetResult(true);
        };

        var listenTask = Task.Run(async () =>
        {
            try { await listenerConn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send to both channels
        await PostgresDb.notify(notifierConn, otherChannel, "should_not_receive").RunAsync();
        await PostgresDb.notify(notifierConn, subscribedChannel, "should_receive").RunAsync();

        await Task.WhenAny(done.Task, Task.Delay(5000));

        receivedFromSubscribed.Should().Contain("should_receive");
        receivedFromOther.Should().BeEmpty(); // Not subscribed to other channel

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, subscribedChannel).RunAsync();
    }

    [Fact]
    public async Task ListenUnlisten_MultipleChannels_Works()
    {
        var conn = _fixture.CreateConnection();
        var channel1 = "multi_ch1";
        var channel2 = "multi_ch2";
        var channel3 = "multi_ch3";

        // Subscribe to multiple channels
        await PostgresDb.listen(conn, channel1).RunAsync();
        await PostgresDb.listen(conn, channel2).RunAsync();
        await PostgresDb.listen(conn, channel3).RunAsync();

        // Unsubscribe from one
        await PostgresDb.unlisten(conn, channel2).RunAsync();

        // Unsubscribe from all remaining
        await PostgresDb.unlisten(conn, channel1).RunAsync();
        await PostgresDb.unlisten(conn, channel3).RunAsync();

        // Should not throw
    }

    [Fact]
    public async Task Notify_EmptyPayload_Works()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "empty_payload_channel";
        var received = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(listenerConn, channel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
                received.TrySetResult(args.Payload);
        };

        var listenTask = Task.Run(async () =>
        {
            try { await listenerConn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification with empty payload
        await PostgresDb.notify(notifierConn, channel, "").RunAsync();

        var payload = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task
            ? await received.Task
            : null;

        payload.Should().Be("");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();
    }

    [Fact]
    public async Task Notify_WithoutPayload_Works()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "no_payload_channel";
        var received = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(listenerConn, channel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
                received.TrySetResult(true);
        };

        var listenTask = Task.Run(async () =>
        {
            try { await listenerConn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification without explicit payload (uses default empty)
        await PostgresDb.notify(notifierConn, channel).RunAsync();

        var wasReceived = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task;
        wasReceived.Should().BeTrue();

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();
    }

    // ==================== Notification Stream (IAsyncEnumerable) ====================

    [Fact]
    public async Task Notifications_Property_ReturnsAsyncEnumerable()
    {
        var conn = _fixture.CreateConnection();

        var query = PostgresDb.notifications(conn);
        var stream = await query.RunAsync();

        stream.Should().NotBeNull();
        stream.Should().BeAssignableTo<IAsyncEnumerable<NpgsqlNotificationEventArgs>>();
    }

    [Fact]
    public async Task Unlisten_AfterListen_StopsReceivingNotifications()
    {
        var listenerConn = _fixture.CreateConnection();
        var notifierConn = _fixture.CreateConnection();
        var channel = "unlisten_test";
        var receivedCount = 0;
        var firstReceived = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(listenerConn, channel).RunAsync();

        listenerConn.Notification += (_, args) =>
        {
            if (args.Channel == channel)
            {
                receivedCount++;
                firstReceived.TrySetResult(true);
            }
        };

        // Start listening
        var listenTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                    await listenerConn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send first notification
        await PostgresDb.notify(notifierConn, channel, "first").RunAsync();
        await Task.WhenAny(firstReceived.Task, Task.Delay(3000));

        // Cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(listenerConn, channel).RunAsync();

        var countAfterUnlisten = receivedCount;

        // Send another notification
        await PostgresDb.notify(notifierConn, channel, "second").RunAsync();

        // Give some time for potential notification
        await Task.Delay(500);

        // Count should not have increased after unlisten
        receivedCount.Should().Be(countAfterUnlisten);
    }
}
