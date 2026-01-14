namespace TheUtils.DbPostgresqlTests;

using FluentAssertions;
using LanguageExt;
using Npgsql;
using Xunit;
using static LanguageExt.Prelude;
using static TheUtils.Db;

/// <summary>
/// Tests for PostgreSQL LISTEN/NOTIFY notification streaming.
/// </summary>
[Collection("PostgreSQL")]
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
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "test_channel_basic";
        var receivedPayload = "";
        var received = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        // Set up listener
        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        // Get the connection to add notification handler
        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
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
            try { await conn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification
        await PostgresDb.notify(channel, "hello_world").Run(notifierEnv).RunAsync();

        // Wait for notification (with timeout)
        var completedInTime = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task;

        completedInTime.Should().BeTrue("Notification should be received within timeout");
        receivedPayload.Should().Be("hello_world");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await waitTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();
    }

    [Fact]
    public async Task Notifications_ReceivesMultipleMessages()
    {
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "test_channel_multi";
        var receivedMessages = new List<string>();
        var messagesReceived = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
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
                    await conn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send multiple notifications
        await PostgresDb.notify(channel, "message1").Run(notifierEnv).RunAsync();
        await PostgresDb.notify(channel, "message2").Run(notifierEnv).RunAsync();
        await PostgresDb.notify(channel, "message3").Run(notifierEnv).RunAsync();

        var completed = await Task.WhenAny(messagesReceived.Task, Task.Delay(5000)) == messagesReceived.Task;

        completed.Should().BeTrue();
        receivedMessages.Should().HaveCount(3);
        receivedMessages.Should().Contain("message1");
        receivedMessages.Should().Contain("message2");
        receivedMessages.Should().Contain("message3");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();
    }

    [Fact]
    public async Task Notifications_WithPayload_ParsesCorrectly()
    {
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "test_channel_payload";
        var receivedPayloads = new List<string>();
        var done = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
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
                    await conn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send payloads with different content
        await PostgresDb.notify(channel, """{"type":"event","data":123}""").Run(notifierEnv).RunAsync();
        await PostgresDb.notify(channel, "simple_text_payload").Run(notifierEnv).RunAsync();

        await Task.WhenAny(done.Task, Task.Delay(5000));

        receivedPayloads.Should().Contain("""{"type":"event","data":123}""");
        receivedPayloads.Should().Contain("simple_text_payload");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();
    }

    [Fact]
    public async Task Notifications_DifferentChannels_OnlyReceivesSubscribed()
    {
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var subscribedChannel = "subscribed_channel";
        var otherChannel = "other_channel";
        var receivedFromSubscribed = new List<string>();
        var receivedFromOther = new List<string>();
        var done = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        // Only listen to one channel
        await PostgresDb.listen(subscribedChannel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
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
            try { await conn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send to both channels
        await PostgresDb.notify(otherChannel, "should_not_receive").Run(notifierEnv).RunAsync();
        await PostgresDb.notify(subscribedChannel, "should_receive").Run(notifierEnv).RunAsync();

        await Task.WhenAny(done.Task, Task.Delay(5000));

        receivedFromSubscribed.Should().Contain("should_receive");
        receivedFromOther.Should().BeEmpty(); // Not subscribed to other channel

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(subscribedChannel).Run(listenerEnv).RunAsync();
    }

    [Fact]
    public async Task ListenUnlisten_MultipleChannels_Works()
    {
        var env = _fixture.CreateDbEnvWithConnection();
        var channel1 = "multi_ch1";
        var channel2 = "multi_ch2";
        var channel3 = "multi_ch3";

        // Subscribe to multiple channels
        await PostgresDb.listen(channel1).Run(env).RunAsync();
        await PostgresDb.listen(channel2).Run(env).RunAsync();
        await PostgresDb.listen(channel3).Run(env).RunAsync();

        // Unsubscribe from one
        await PostgresDb.unlisten(channel2).Run(env).RunAsync();

        // Unsubscribe from all remaining
        await PostgresDb.unlisten(channel1).Run(env).RunAsync();
        await PostgresDb.unlisten(channel3).Run(env).RunAsync();

        // Should not throw
    }

    [Fact]
    public async Task Notify_EmptyPayload_Works()
    {
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "empty_payload_channel";
        var received = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
        {
            if (args.Channel == channel)
                received.TrySetResult(args.Payload);
        };

        var listenTask = Task.Run(async () =>
        {
            try { await conn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification with empty payload
        await PostgresDb.notify(channel, "").Run(notifierEnv).RunAsync();

        var payload = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task
            ? await received.Task
            : null;

        payload.Should().Be("");

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();
    }

    [Fact]
    public async Task Notify_WithoutPayload_Works()
    {
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "no_payload_channel";
        var received = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
        {
            if (args.Channel == channel)
                received.TrySetResult(true);
        };

        var listenTask = Task.Run(async () =>
        {
            try { await conn.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Send notification without explicit payload (uses default empty)
        await PostgresDb.notify(channel).Run(notifierEnv).RunAsync();

        var wasReceived = await Task.WhenAny(received.Task, Task.Delay(5000)) == received.Task;
        wasReceived.Should().BeTrue();

        // Cleanup - cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();
    }

    // ==================== Notification Stream (IAsyncEnumerable) ====================

    [Fact]
    public async Task Notifications_Property_ReturnsAsyncEnumerable()
    {
        var env = _fixture.CreateDbEnvWithConnection();

        var query = PostgresDb.notifications;
        var stream = await query.Run(env).RunAsync();

        stream.Should().NotBeNull();
        stream.Should().BeAssignableTo<IAsyncEnumerable<NpgsqlNotificationEventArgs>>();
    }

    [Fact]
    public async Task Unlisten_AfterListen_StopsReceivingNotifications()
    {
        var listenerEnv = _fixture.CreateDbEnvWithConnection();
        var notifierEnv = _fixture.CreateDbEnvWithConnection();
        var channel = "unlisten_test";
        var receivedCount = 0;
        var firstReceived = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource();

        await PostgresDb.listen(channel).Run(listenerEnv).RunAsync();

        var conn = listenerEnv.Connection as NpgsqlConnection;
        conn!.Notification += (_, args) =>
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
                    await conn.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        });

        // Send first notification
        await PostgresDb.notify(channel, "first").Run(notifierEnv).RunAsync();
        await Task.WhenAny(firstReceived.Task, Task.Delay(3000));

        // Cancel wait first, then unlisten
        await cts.CancelAsync();
        await listenTask;
        await PostgresDb.unlisten(channel).Run(listenerEnv).RunAsync();

        var countAfterUnlisten = receivedCount;

        // Send another notification
        await PostgresDb.notify(channel, "second").Run(notifierEnv).RunAsync();

        // Give some time for potential notification
        await Task.Delay(500);

        // Count should not have increased after unlisten
        receivedCount.Should().Be(countAfterUnlisten);
    }
}
