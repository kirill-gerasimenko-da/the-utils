namespace TheUtils;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Npgsql;

/// <summary>
/// Extension methods for Npgsql notifications.
/// </summary>
public static class PostgresDbExtensions
{
    /// <summary>
    /// Convert Npgsql notifications to an async enumerable stream.
    /// </summary>
    public static async IAsyncEnumerable<NpgsqlNotificationEventArgs> ToNotificationStream(
        this NpgsqlConnection conn,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var channel = Channel.CreateUnbounded<NpgsqlNotificationEventArgs>();

        void OnNotification(object sender, NpgsqlNotificationEventArgs e) =>
            channel.Writer.TryWrite(e);

        conn.Notification += OnNotification;
        Task? waitTask = null;

        try
        {
            waitTask = Task.Run(
                async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                            await conn.WaitAsync(ct);
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                },
                ct
            );

            await foreach (var notification in channel.Reader.ReadAllAsync(ct))
                yield return notification;
        }
        finally
        {
            if (waitTask != null)
            {
                try { await waitTask; }
                catch (OperationCanceledException) { }
            }

            conn.Notification -= OnNotification;
        }
    }
}
