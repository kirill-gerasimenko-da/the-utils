namespace TheUtils;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Npgsql;

/// <summary>
/// Extension methods for Npgsql notifications.
/// </summary>
public static class NpgsqlNotificationExtensions
{
    /// <summary>
    /// Convert Npgsql notifications to an async enumerable stream.
    /// </summary>
    public static async IAsyncEnumerable<NpgsqlNotificationEventArgs> ToNotificationStream(
        this NpgsqlConnection conn,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<NpgsqlNotificationEventArgs>();

        void OnNotification(object sender, NpgsqlNotificationEventArgs e) =>
            channel.Writer.TryWrite(e);

        conn.Notification += OnNotification;

        try
        {
            // Keep connection alive to receive notifications
            var waitTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await conn.WaitAsync(ct);
                }
            }, ct);

            await foreach (var notification in channel.Reader.ReadAllAsync(ct))
                yield return notification;
        }
        finally
        {
            conn.Notification -= OnNotification;
        }
    }
}
