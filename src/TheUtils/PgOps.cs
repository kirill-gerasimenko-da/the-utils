namespace TheUtils;

using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;
using Npgsql;
using static LanguageExt.Prelude;

/// <summary>
/// PostgreSQL monad operations - queries, transactions, and Npgsql-specific features.
/// </summary>
public static class PgOps
{
    // ==================== Environment & State Access ====================

    /// <summary>
    /// Access the current environment.
    /// </summary>
    public static Pg<PgEnv> env => Pg.Asks<PgEnv>(identity).As();

    /// <summary>
    /// Access the DbContext from environment.
    /// </summary>
    public static Pg<DbContext> context =>
        from e in env
        select e.Context;

    /// <summary>
    /// Access the DatabaseFacade for raw operations.
    /// </summary>
    public static Pg<DatabaseFacade> facade =>
        from c in context
        select c.Database;

    /// <summary>
    /// Access the current state.
    /// </summary>
    public static Pg<PgState> state => Stateful.get<Pg, PgState>().As();

    /// <summary>
    /// Replace the current state.
    /// </summary>
    public static Pg<Unit> setState(PgState s) => Stateful.put<Pg, PgState>(s).As();

    /// <summary>
    /// Modify the current state.
    /// </summary>
    public static Pg<Unit> modifyState(Func<PgState, PgState> f) => Stateful.modify<Pg, PgState>(f).As();

    // ==================== IO Lifting ====================

    /// <summary>
    /// Lift an IO operation into Pg.
    /// </summary>
    public static Pg<A> liftIO<A>(IO<A> io) => Pg.LiftIO(io).As();

    /// <summary>
    /// Lift an async operation into Pg.
    /// </summary>
    public static Pg<A> liftIO<A>(Func<EnvIO, Task<A>> f) =>
        liftIO(IO.liftAsync(f));

    /// <summary>
    /// Lift a synchronous operation into Pg.
    /// </summary>
    public static Pg<A> liftIO<A>(Func<A> f) =>
        liftIO(IO.lift(f));

    // ==================== Pure & Fail ====================

    /// <summary>
    /// Lift a pure value into Pg.
    /// </summary>
    public static Pg<A> pure<A>(A value) => Applicative.pure<Pg, A>(value).As();

    /// <summary>
    /// Fail with an error.
    /// </summary>
    public static Pg<A> fail<A>(Error error) => Pg.Fail<A>(error).As();

    /// <summary>
    /// Fail with a string message.
    /// </summary>
    public static Pg<A> fail<A>(string message) => fail<A>(Error.New(message));

    // ==================== Query Operations (EF Core) ====================

    /// <summary>
    /// Execute a LINQ query and return results as Seq.
    /// </summary>
    public static Pg<Seq<A>> seq<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<List<A>>(io => query.ToListAsync(io.Token))
        from __ in modifyState(s => s.IncrementOps())
        select toSeq(r);

    /// <summary>
    /// Execute an interpolated SQL query and return results as Seq.
    /// </summary>
    public static Pg<Seq<A>> seq<A>(FormattableString sql) =>
        from q in queryable<A>(sql)
        from r in seq(q)
        select r;

    /// <summary>
    /// Execute a raw SQL query with parameters and return results as Seq.
    /// </summary>
    public static Pg<Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        from q in queryable<A>(sql, @params)
        from r in seq(q)
        select r;

    /// <summary>
    /// Check if any rows match the query.
    /// </summary>
    public static Pg<bool> any<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<bool>(io => query.AnyAsync(io.Token))
        from __ in modifyState(s => s.IncrementOps())
        select r;

    /// <summary>
    /// Check if any rows match the interpolated SQL.
    /// </summary>
    public static Pg<bool> any<A>(FormattableString sql) =>
        from q in queryable<A>(sql)
        from r in any(q)
        select r;

    /// <summary>
    /// Count rows matching the query.
    /// </summary>
    public static Pg<int> count<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<int>(io => query.CountAsync(io.Token))
        from __ in modifyState(s => s.IncrementOps())
        select r;

    /// <summary>
    /// Count rows matching the interpolated SQL.
    /// </summary>
    public static Pg<int> count<A>(FormattableString sql) =>
        from q in queryable<A>(sql)
        from r in count(q)
        select r;

    /// <summary>
    /// Get the first row or None.
    /// </summary>
    public static Pg<Option<A>> head<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.FirstOrDefaultAsync(io.Token)!)
        from __ in modifyState(s => s.IncrementOps())
        select Optional(r);

    /// <summary>
    /// Get the first row from interpolated SQL or None.
    /// </summary>
    public static Pg<Option<A>> head<A>(FormattableString sql) =>
        from q in queryable<A>(sql)
        from r in head(q)
        select r;

    /// <summary>
    /// Get exactly one row (throws if not exactly one).
    /// </summary>
    public static Pg<A> single<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.SingleAsync(io.Token))
        from __ in modifyState(s => s.IncrementOps())
        select r;

    /// <summary>
    /// Get exactly one row from interpolated SQL.
    /// </summary>
    public static Pg<A> single<A>(FormattableString sql) =>
        from q in queryable<A>(sql)
        from r in single(q)
        select r;

    /// <summary>
    /// Get a DbSet for the entity type.
    /// </summary>
    public static Pg<DbSet<A>> set<A>() where A : class =>
        from c in context
        select c.Set<A>();

    /// <summary>
    /// Create a queryable from interpolated SQL.
    /// </summary>
    public static Pg<IQueryable<A>> queryable<A>(FormattableString sql) =>
        from f in facade
        select f.SqlQuery<A>(sql);

    /// <summary>
    /// Create a queryable from raw SQL with parameters.
    /// </summary>
    public static Pg<IQueryable<A>> queryable<A>(string sql, Seq<object> @params = default) =>
        from f in facade
        select f.SqlQueryRaw<A>(sql, @params.ToArray());

    // ==================== Entity Operations ====================

    /// <summary>
    /// Add an entity to the context.
    /// </summary>
    public static Pg<EntityEntry<A>> add<A>(A entity) where A : class =>
        from s in set<A>()
        from e in liftIO<EntityEntry<A>>(io => s.AddAsync(entity, io.Token).AsTask())
        from _ in modifyState(st => st.IncrementOps())
        select e;

    /// <summary>
    /// Add multiple entities.
    /// </summary>
    public static Pg<Unit> addRange<A>(Seq<A> entities) where A : class =>
        from s in set<A>()
        from _ in liftIO<Unit>(async io =>
        {
            await s.AddRangeAsync(entities, io.Token);
            return unit;
        })
        from __ in modifyState(st => st.IncrementOps())
        select unit;

    /// <summary>
    /// Update an entity.
    /// </summary>
    public static Pg<EntityEntry<A>> update<A>(A entity) where A : class =>
        from s in set<A>()
        from _ in modifyState(st => st.IncrementOps())
        select s.Update(entity);

    /// <summary>
    /// Update multiple entities.
    /// </summary>
    public static Pg<Unit> updateRange<A>(Seq<A> entities) where A : class =>
        from s in set<A>()
        from _ in liftIO<Unit>(() =>
        {
            s.UpdateRange(entities);
            return unit;
        })
        from __ in modifyState(st => st.IncrementOps())
        select unit;

    /// <summary>
    /// Delete an entity.
    /// </summary>
    public static Pg<EntityEntry<A>> delete<A>(A entity) where A : class =>
        from s in set<A>()
        from _ in modifyState(st => st.IncrementOps())
        select s.Remove(entity);

    /// <summary>
    /// Delete multiple entities.
    /// </summary>
    public static Pg<Unit> deleteRange<A>(Seq<A> entities) where A : class =>
        from s in set<A>()
        from _ in liftIO<Unit>(() =>
        {
            s.RemoveRange(entities);
            return unit;
        })
        from __ in modifyState(st => st.IncrementOps())
        select unit;

    /// <summary>
    /// Save all pending changes.
    /// </summary>
    public static Pg<int> saveChanges =>
        from c in context
        from n in liftIO<int>(io => c.SaveChangesAsync(io.Token))
        select n;

    // ==================== Raw SQL Execution ====================

    /// <summary>
    /// Execute interpolated SQL (INSERT, UPDATE, DELETE).
    /// </summary>
    public static Pg<int> execute(FormattableString sql) =>
        from f in facade
        from n in liftIO<int>(io => f.ExecuteSqlAsync(sql, io.Token))
        from _ in modifyState(s => s.IncrementOps())
        select n;

    /// <summary>
    /// Execute raw SQL with parameters.
    /// </summary>
    public static Pg<int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO<int>(io => f.ExecuteSqlRawAsync(sql, @params.ToArray(), io.Token))
        from _ in modifyState(s => s.IncrementOps())
        select n;

    // ==================== Transaction Management ====================

    /// <summary>
    /// Begin a new transaction with optional isolation level.
    /// </summary>
    public static Pg<Unit> beginTransaction(IsolationLevel? level = null) =>
        from e in env
        from s in state
        from t in liftIO<IDbContextTransaction>(io => e.Context.Database.BeginTransactionAsync(
            level ?? e.DefaultIsolation, io.Token))
        from _ in setState(s with { Transaction = Some(t) })
        select unit;

    /// <summary>
    /// Commit the current transaction.
    /// </summary>
    public static Pg<Unit> commit =>
        from s in state
        from _ in s.Transaction.Match(
            Some: t => liftIO<Unit>(async io =>
            {
                await t.CommitAsync(io.Token);
                return unit;
            }),
            None: () => pure(unit))
        from __ in modifyState(st => st.ClearTransaction())
        select unit;

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    public static Pg<Unit> rollback =>
        from s in state
        from _ in s.Transaction.Match(
            Some: t => liftIO<Unit>(async io =>
            {
                await t.RollbackAsync(io.Token);
                return unit;
            }),
            None: () => pure(unit))
        from __ in modifyState(st => st.ClearTransaction())
        select unit;

    /// <summary>
    /// Execute an operation within a transaction with automatic commit/rollback.
    /// </summary>
    public static Pg<A> transact<A>(Pg<A> operation, IsolationLevel? level = null) =>
        from _ in beginTransaction(level)
        from r in Pg.Catch(operation, _ => true, e =>
            from __ in rollback
            from ___ in fail<A>(e)
            select default(A)!).As()
        from __ in commit
        select r;

    // ==================== Npgsql-Specific: COPY Protocol ====================

    /// <summary>
    /// Begin a binary COPY import for bulk data loading.
    /// </summary>
    public static Pg<NpgsqlBinaryImporter> beginBinaryImport(string copyCommand) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            await e.Connection.OpenAsync(io.Token);
            return unit;
        })
        from importer in liftIO<NpgsqlBinaryImporter>(io =>
            e.Connection.BeginBinaryImportAsync(copyCommand, io.Token))
        select importer;

    /// <summary>
    /// Bulk import rows using COPY protocol.
    /// </summary>
    public static Pg<ulong> binaryImport<A>(
        string table,
        Seq<A> rows,
        Action<NpgsqlBinaryImporter, A> writeRow) =>
        from e in env
        from count in liftIO<ulong>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var writer = await e.Connection.BeginBinaryImportAsync(
                $"COPY {table} FROM STDIN (FORMAT BINARY)", io.Token);
            foreach (var row in rows)
            {
                await writer.StartRowAsync(io.Token);
                writeRow(writer, row);
            }
            return await writer.CompleteAsync(io.Token);
        })
        from _ in modifyState(s => s.IncrementOps())
        select count;

    /// <summary>
    /// Begin a binary COPY export for bulk data reading.
    /// </summary>
    public static Pg<NpgsqlBinaryExporter> beginBinaryExport(string copyCommand) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            await e.Connection.OpenAsync(io.Token);
            return unit;
        })
        from exporter in liftIO<NpgsqlBinaryExporter>(io =>
            e.Connection.BeginBinaryExportAsync(copyCommand, io.Token))
        select exporter;

    // ==================== Npgsql-Specific: LISTEN/NOTIFY ====================

    /// <summary>
    /// Start listening on a notification channel.
    /// </summary>
    public static Pg<Unit> listen(string channel) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = $"LISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Stop listening on a notification channel.
    /// </summary>
    public static Pg<Unit> unlisten(string channel) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = $"UNLISTEN {channel}";
            await cmd.ExecuteNonQueryAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Send a notification on a channel.
    /// </summary>
    public static Pg<Unit> notify(string channel, string payload = "") =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            await using var cmd = e.Connection.CreateCommand();
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
    public static Pg<IAsyncEnumerable<NpgsqlNotificationEventArgs>> notifications =>
        from e in env
        select e.Connection.ToNotificationStream();

    // ==================== Npgsql-Specific: Advisory Locks ====================

    /// <summary>
    /// Try to acquire an advisory lock (non-blocking).
    /// </summary>
    public static Pg<bool> tryAdvisoryLock(long key) =>
        from e in env
        from result in liftIO<bool>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = $"SELECT pg_try_advisory_lock({key})";
            return (bool)(await cmd.ExecuteScalarAsync(io.Token))!;
        })
        select result;

    /// <summary>
    /// Acquire an advisory lock (blocking).
    /// </summary>
    public static Pg<Unit> advisoryLock(long key) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_lock({key})";
            await cmd.ExecuteScalarAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Release an advisory lock.
    /// </summary>
    public static Pg<Unit> advisoryUnlock(long key) =>
        from e in env
        from _ in liftIO<Unit>(async io =>
        {
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_unlock({key})";
            await cmd.ExecuteScalarAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Execute an operation while holding an advisory lock.
    /// </summary>
    public static Pg<A> withAdvisoryLock<A>(long key, Pg<A> operation) =>
        from _ in advisoryLock(key)
        from r in Pg.Catch(operation, _ => true, e =>
            from __ in advisoryUnlock(key)
            from ___ in fail<A>(e)
            select default(A)!).As()
        from ___ in advisoryUnlock(key)
        select r;

    // ==================== Npgsql-Specific: Raw Queries ====================

    /// <summary>
    /// Execute a raw query with custom row mapping.
    /// </summary>
    public static Pg<Seq<A>> rawQuery<A>(
        string sql,
        Func<NpgsqlDataReader, A> mapper,
        params NpgsqlParameter[] parameters) =>
        from e in env
        from results in liftIO<Seq<A>>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            await using var reader = await cmd.ExecuteReaderAsync(io.Token);
            var list = new List<A>();
            while (await reader.ReadAsync(io.Token))
                list.Add(mapper(reader));
            return toSeq(list);
        })
        from _ in modifyState(s => s.IncrementOps())
        select results;

    /// <summary>
    /// Execute a raw scalar query.
    /// </summary>
    public static Pg<Option<A>> rawScalar<A>(string sql, params NpgsqlParameter[] parameters) =>
        from e in env
        from result in liftIO<Option<A>>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            var scalar = await cmd.ExecuteScalarAsync(io.Token);
            return scalar == null || scalar == DBNull.Value
                ? Option<A>.None
                : Some((A)scalar);
        })
        from _ in modifyState(s => s.IncrementOps())
        select result;

    // ==================== Npgsql-Specific: JSONB ====================

    /// <summary>
    /// Query JSONB data using jsonb_path_query_first.
    /// </summary>
    public static Pg<Option<A>> jsonbPath<A>(
        string table,
        string jsonColumn,
        string jsonPath,
        object vars = null!) =>
        from e in env
        from result in liftIO<Option<A>>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = vars == null
                ? $"SELECT jsonb_path_query_first({jsonColumn}, $1) FROM {table}"
                : $"SELECT jsonb_path_query_first({jsonColumn}, $1, $2) FROM {table}";
            cmd.Parameters.AddWithValue(jsonPath);
            if (vars != null)
                cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, vars);
            var scalar = await cmd.ExecuteScalarAsync(io.Token);
            return scalar == null || scalar == DBNull.Value
                ? Option<A>.None
                : Some(JsonConvert.DeserializeObject<A>(scalar.ToString()!)!);
        })
        from _ in modifyState(s => s.IncrementOps())
        select result;
}

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
