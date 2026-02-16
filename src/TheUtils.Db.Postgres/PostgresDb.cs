namespace TheUtils;

using System.Data;
using LanguageExt;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using static LanguageExt.Prelude;

/// <summary>
/// Postgres-specific IO operations.
/// Provides COPY protocol, LISTEN/NOTIFY, advisory locks, raw queries, and JSONB support.
/// All methods return IO&lt;A&gt; — use Db.liftIO to compose with Db monad chains.
/// </summary>
public static class PostgresDb
{
    // ==================== Connection Helpers ====================

    private static IO<Unit> ensureOpen(NpgsqlConnection conn) =>
        IO.liftAsync(async env =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(env.Token);
            return unit;
        });

    // ==================== COPY Protocol ====================

    /// <summary>
    /// Begin a binary COPY import for bulk data loading.
    /// </summary>
    public static IO<NpgsqlBinaryImporter> beginBinaryImport(
        NpgsqlConnection conn,
        string copyCommand
    ) =>
        from _ in ensureOpen(conn)
        from importer in IO.liftAsync(env => conn.BeginBinaryImportAsync(copyCommand, env.Token))
        select importer;

    /// <summary>
    /// Bulk import rows using COPY protocol.
    /// </summary>
    public static IO<ulong> binaryImport<A>(
        NpgsqlConnection conn,
        string table,
        Seq<A> rows,
        Action<NpgsqlBinaryImporter, A> writeRow
    ) =>
        IO.liftAsync(async env =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(env.Token);
            await using var writer = await conn.BeginBinaryImportAsync(
                $"COPY {table} FROM STDIN (FORMAT BINARY)",
                env.Token
            );
            foreach (var row in rows)
            {
                await writer.StartRowAsync(env.Token);
                writeRow(writer, row);
            }
            return await writer.CompleteAsync(env.Token);
        });

    /// <summary>
    /// Begin a binary COPY export for bulk data reading.
    /// </summary>
    public static IO<NpgsqlBinaryExporter> beginBinaryExport(
        NpgsqlConnection conn,
        string copyCommand
    ) =>
        from _ in ensureOpen(conn)
        from exporter in IO.liftAsync(env => conn.BeginBinaryExportAsync(copyCommand, env.Token))
        select exporter;

    // ==================== LISTEN/NOTIFY ====================

    /// <summary>
    /// Start listening on a notification channel.
    /// </summary>
    public static IO<Unit> listen(NpgsqlConnection conn, string channel) =>
        from _ in ensureOpen(conn)
        from __ in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"LISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(env.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Stop listening on a notification channel.
    /// </summary>
    public static IO<Unit> unlisten(NpgsqlConnection conn, string channel) =>
        IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UNLISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(env.Token);
            return unit;
        });

    /// <summary>
    /// Send a notification on a channel.
    /// </summary>
    public static IO<Unit> notify(NpgsqlConnection conn, string channel, string payload = "") =>
        from _ in ensureOpen(conn)
        from __ in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = string.IsNullOrEmpty(payload)
                ? $"NOTIFY {channel}"
                : $"NOTIFY {channel}, '{payload.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync(env.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Get an async enumerable of notifications.
    /// </summary>
    public static IO<IAsyncEnumerable<NpgsqlNotificationEventArgs>> notifications(
        NpgsqlConnection conn
    ) => IO.pure(conn.ToNotificationStream());

    // ==================== Advisory Locks ====================

    /// <summary>
    /// Try to acquire an advisory lock (non-blocking).
    /// </summary>
    public static IO<bool> tryAdvisoryLock(NpgsqlConnection conn, long key) =>
        from _ in ensureOpen(conn)
        from result in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_try_advisory_lock({key})";
            return (bool)(await cmd.ExecuteScalarAsync(env.Token))!;
        })
        select result;

    /// <summary>
    /// Acquire an advisory lock (blocking).
    /// </summary>
    public static IO<Unit> advisoryLock(NpgsqlConnection conn, long key) =>
        from _ in ensureOpen(conn)
        from __ in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_lock({key})";
            await cmd.ExecuteScalarAsync(env.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Release an advisory lock.
    /// </summary>
    public static IO<Unit> advisoryUnlock(NpgsqlConnection conn, long key) =>
        IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_unlock({key})";
            await cmd.ExecuteScalarAsync(env.Token);
            return unit;
        });

    /// <summary>
    /// Execute an IO operation while holding an advisory lock.
    /// Releases lock on completion or error.
    /// </summary>
    public static IO<A> withAdvisoryLock<A>(NpgsqlConnection conn, long key, IO<A> operation) =>
        from _ in advisoryLock(conn, key)
        from result in operation.Catch(
            _ => true,
            err => advisoryUnlock(conn, key).Bind(_ => IO.fail<A>(err))
        )
        from __ in advisoryUnlock(conn, key)
        select result;

    /// <summary>
    /// Execute a Db operation while holding an advisory lock.
    /// Convenience overload that extracts IO from the Db operation.
    /// Releases lock on completion or error.
    /// </summary>
    public static Db<A> withAdvisoryLock<A>(NpgsqlConnection conn, long key, Db<A> operation) =>
        from operationIO in Db.ToIO(operation).As()
        from result in Db.liftIO(withAdvisoryLock(conn, key, operationIO))
        select result;

    // ==================== Raw Npgsql Queries ====================

    /// <summary>
    /// Execute a raw query with custom row mapping.
    /// </summary>
    public static IO<Seq<A>> rawQuery<A>(
        NpgsqlConnection conn,
        string sql,
        Func<NpgsqlDataReader, A> mapper,
        params NpgsqlParameter[] parameters
    ) =>
        from _ in ensureOpen(conn)
        from results in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            await using var reader = await cmd.ExecuteReaderAsync(env.Token);
            var list = new List<A>();
            while (await reader.ReadAsync(env.Token))
                list.Add(mapper(reader));
            return toSeq(list).Strict();
        })
        select results;

    /// <summary>
    /// Execute a raw scalar query.
    /// </summary>
    public static IO<Option<A>> rawScalar<A>(
        NpgsqlConnection conn,
        string sql,
        params NpgsqlParameter[] parameters
    ) =>
        from _ in ensureOpen(conn)
        from result in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            var scalar = await cmd.ExecuteScalarAsync(env.Token);
            return scalar == null || scalar == DBNull.Value ? Option<A>.None : Some((A)scalar);
        })
        select result;

    // ==================== JSONB ====================

    /// <summary>
    /// Query JSONB data using jsonb_path_query_first.
    /// </summary>
    public static IO<Option<A>> jsonbPath<A>(
        NpgsqlConnection conn,
        string table,
        string jsonColumn,
        string jsonPath,
        Option<object> vars = default
    ) =>
        from _ in ensureOpen(conn)
        from result in IO.liftAsync(async env =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = vars.IsNone
                ? $"SELECT jsonb_path_query_first({jsonColumn}, $1) FROM {table}"
                : $"SELECT jsonb_path_query_first({jsonColumn}, $1, $2) FROM {table}";
            cmd.Parameters.Add(
                new NpgsqlParameter { Value = jsonPath, NpgsqlDbType = NpgsqlDbType.JsonPath }
            );
            vars.IfSome(v =>
                cmd.Parameters.Add(
                    new NpgsqlParameter { Value = v, NpgsqlDbType = NpgsqlDbType.Jsonb }
                )
            );
            var scalar = await cmd.ExecuteScalarAsync(env.Token);

            // Handle SQL NULL (no match found)
            if (scalar == null || scalar == DBNull.Value)
                return Option<A>.None;

            var scalarString = scalar.ToString();

            // Handle JSON null value (jsonb_path_query_first returns "null" for JSON null)
            if (scalarString == "null")
                return Option<A>.None;

            return Some(JsonConvert.DeserializeObject<A>(scalarString!)!);
        })
        select result;
}
