namespace TheUtils;

using System.Data;
using LanguageExt;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using static LanguageExt.Prelude;

/// <summary>
/// PostgreSQL-specific extensions for the Db monad.
/// Provides COPY protocol, LISTEN/NOTIFY, advisory locks, raw queries, and JSONB support.
/// </summary>
public static class PostgresDb
{
    // ==================== Helper ====================

    /// <summary>
    /// Gets NpgsqlConnection from DbEnv, throwing if not available.
    /// </summary>
    private static NpgsqlConnection GetNpgsqlConnection(DbEnv env) =>
        env.Connection as NpgsqlConnection
        ?? throw new InvalidOperationException(
            "PostgreSQL extensions require an NpgsqlConnection. " +
            "Ensure DbContext is configured with Npgsql provider.");

    // ==================== COPY Protocol ====================

    /// <summary>
    /// Begin a binary COPY import for bulk data loading.
    /// </summary>
    public static Db<NpgsqlBinaryImporter> beginBinaryImport(string copyCommand) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            return unit;
        })
        from importer in Db.liftIO<NpgsqlBinaryImporter>(io =>
            conn.BeginBinaryImportAsync(copyCommand, io.Token))
        select importer;

    /// <summary>
    /// Bulk import rows using COPY protocol.
    /// </summary>
    public static Db<ulong> binaryImport<A>(
        string table,
        Seq<A> rows,
        Action<NpgsqlBinaryImporter, A> writeRow) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from count in Db.liftIO<ulong>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var writer = await conn.BeginBinaryImportAsync(
                $"COPY {table} FROM STDIN (FORMAT BINARY)", io.Token);
            foreach (var row in rows)
            {
                await writer.StartRowAsync(io.Token);
                writeRow(writer, row);
            }
            return await writer.CompleteAsync(io.Token);
        })
        select count;

    /// <summary>
    /// Begin a binary COPY export for bulk data reading.
    /// </summary>
    public static Db<NpgsqlBinaryExporter> beginBinaryExport(string copyCommand) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            return unit;
        })
        from exporter in Db.liftIO<NpgsqlBinaryExporter>(io =>
            conn.BeginBinaryExportAsync(copyCommand, io.Token))
        select exporter;

    // ==================== LISTEN/NOTIFY ====================

    /// <summary>
    /// Start listening on a notification channel.
    /// </summary>
    public static Db<Unit> listen(string channel) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"LISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Stop listening on a notification channel.
    /// </summary>
    public static Db<Unit> unlisten(string channel) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UNLISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Send a notification on a channel.
    /// </summary>
    public static Db<Unit> notify(string channel, string payload = "") =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = string.IsNullOrEmpty(payload)
                ? $"NOTIFY {channel}"
                : $"NOTIFY {channel}, '{payload.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Get an async enumerable of notifications.
    /// </summary>
    public static Db<IAsyncEnumerable<NpgsqlNotificationEventArgs>> notifications =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        select conn.ToNotificationStream();

    // ==================== Advisory Locks ====================

    /// <summary>
    /// Try to acquire an advisory lock (non-blocking).
    /// </summary>
    public static Db<bool> tryAdvisoryLock(long key) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from result in Db.liftIO<bool>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_try_advisory_lock({key})";
            return (bool)(await cmd.ExecuteScalarAsync(io.Token))!;
        })
        select result;

    /// <summary>
    /// Acquire an advisory lock (blocking).
    /// </summary>
    public static Db<Unit> advisoryLock(long key) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_lock({key})";
            await cmd.ExecuteScalarAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Release an advisory lock.
    /// </summary>
    public static Db<Unit> advisoryUnlock(long key) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in Db.liftIO<Unit>(async io =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_unlock({key})";
            await cmd.ExecuteScalarAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Execute an operation while holding an advisory lock.
    /// Releases lock on completion or error.
    /// </summary>
    public static Db<A> withAdvisoryLock<A>(long key, Db<A> operation) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from _ in advisoryLock(key)
        from operationIO in Db.ToIO(operation).As()
        from result in Db.liftIO(
            operationIO.Catch(
                _ => true,
                err => advisoryUnlockIO(key, conn)
                         .Bind(_ => IO.fail<A>(err))
            )
        )
        from __ in Db.liftIO(advisoryUnlockIO(key, conn))
        select result;

    /// <summary>
    /// Release an advisory lock using a captured connection (pure IO).
    /// </summary>
    private static IO<Unit> advisoryUnlockIO(long key, NpgsqlConnection conn) =>
        IO.liftAsync<Unit>(async envIO =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_unlock({key})";
            await cmd.ExecuteScalarAsync(envIO.Token);
            return unit;
        });

    // ==================== Raw Npgsql Queries ====================

    /// <summary>
    /// Execute a raw query with custom row mapping.
    /// </summary>
    public static Db<Seq<A>> rawQuery<A>(
        string sql,
        Func<NpgsqlDataReader, A> mapper,
        params NpgsqlParameter[] parameters) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from results in Db.liftIO<Seq<A>>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            await using var reader = await cmd.ExecuteReaderAsync(io.Token);
            var list = new List<A>();
            while (await reader.ReadAsync(io.Token))
                list.Add(mapper(reader));
            return toSeq(list).Strict();
        })
        select results;

    /// <summary>
    /// Execute a raw scalar query.
    /// </summary>
    public static Db<Option<A>> rawScalar<A>(string sql, params NpgsqlParameter[] parameters) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from result in Db.liftIO<Option<A>>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            var scalar = await cmd.ExecuteScalarAsync(io.Token);
            return scalar == null || scalar == DBNull.Value
                ? Option<A>.None
                : Some((A)scalar);
        })
        select result;

    // ==================== JSONB ====================

    /// <summary>
    /// Query JSONB data using jsonb_path_query_first.
    /// </summary>
    public static Db<Option<A>> jsonbPath<A>(
        string table,
        string jsonColumn,
        string jsonPath,
        Option<object> vars = default) =>
        from e in Db.env
        let conn = GetNpgsqlConnection(e)
        from result in Db.liftIO<Option<A>>(async io =>
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(io.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = vars.IsNone
                ? $"SELECT jsonb_path_query_first({jsonColumn}, $1) FROM {table}"
                : $"SELECT jsonb_path_query_first({jsonColumn}, $1, $2) FROM {table}";
            cmd.Parameters.Add(new NpgsqlParameter { Value = jsonPath, NpgsqlDbType = NpgsqlDbType.JsonPath });
            vars.IfSome(v => cmd.Parameters.Add(new NpgsqlParameter { Value = v, NpgsqlDbType = NpgsqlDbType.Jsonb }));
            var scalar = await cmd.ExecuteScalarAsync(io.Token);
            return scalar == null || scalar == DBNull.Value
                ? Option<A>.None
                : Some(JsonConvert.DeserializeObject<A>(scalar.ToString()!)!);
        })
        select result;
}
