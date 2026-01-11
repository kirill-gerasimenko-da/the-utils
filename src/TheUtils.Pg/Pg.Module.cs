
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
public partial class Pg
{
    // ==================== Environment Access ====================

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
        select toSeq(r).Strict();

    /// <summary>
    /// Execute an interpolated SQL query and return results as Seq.
    /// </summary>
    public static Pg<Seq<A>> seq<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in seq(q)
        select r;

    /// <summary>
    /// Execute a raw SQL query with parameters and return results as Seq.
    /// </summary>
    public static Pg<Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in seq(q)
        select r;

    /// <summary>
    /// Check if any rows match the query.
    /// </summary>
    public static Pg<bool> any<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<bool>(io => query.AnyAsync(io.Token))
        select r;

    /// <summary>
    /// Check if any rows match the interpolated SQL.
    /// </summary>
    public static Pg<bool> any<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in any(q)
        select r;

    /// <summary>
    /// Count rows matching the query.
    /// </summary>
    public static Pg<int> count<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<int>(io => query.CountAsync(io.Token))
        select r;

    /// <summary>
    /// Count rows matching the interpolated SQL.
    /// </summary>
    public static Pg<int> count<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in count(q)
        select r;

    /// <summary>
    /// Get the first row or None.
    /// </summary>
    public static Pg<Option<A>> head<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.FirstOrDefaultAsync(io.Token)!)
        select Optional(r);

    /// <summary>
    /// Get the first row from interpolated SQL or None.
    /// </summary>
    public static Pg<Option<A>> head<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in head(q)
        select r;

    // ==================== headT (OptionT variant) ====================

    /// <summary>
    /// Get the first row as OptionT (for monad transformer chaining).
    /// </summary>
    public static OptionT<Pg, A> headT<A>(IQueryable<A> query) =>
        OptionT.lift(head(query));

    /// <summary>
    /// Get the first row from interpolated SQL as OptionT.
    /// </summary>
    public static OptionT<Pg, A> headT<A>(FormattableString sql) where A : class =>
        OptionT.lift(head<A>(sql));

    /// <summary>
    /// Get exactly one row (throws if not exactly one).
    /// </summary>
    public static Pg<A> single<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.SingleAsync(io.Token))
        select r;

    /// <summary>
    /// Get exactly one row from interpolated SQL.
    /// </summary>
    public static Pg<A> single<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in single(q)
        select r;

    /// <summary>
    /// Get a DbSet for the entity type.
    /// </summary>
    public static Pg<DbSet<A>> set<A>() where A : class =>
        from c in context
        select c.Set<A>();

    /// <summary>
    /// Create a query from interpolated SQL.
    /// </summary>
    public static Pg<IQueryable<A>> query<A>(FormattableString sql) =>
        from f in facade
        select f.SqlQuery<A>(sql);

    /// <summary>
    /// Create a query from raw SQL with parameters.
    /// </summary>
    public static Pg<IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        from f in facade
        select f.SqlQueryRaw<A>(sql, @params.ToArray());

    // ==================== Entity Operations ====================

    /// <summary>
    /// Add an entity to the context.
    /// </summary>
    public static Pg<EntityEntry<A>> add<A>(A entity) where A : class =>
        from s in set<A>()
        from e in liftIO<EntityEntry<A>>(io => s.AddAsync(entity, io.Token).AsTask())
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
        select unit;

    /// <summary>
    /// Update an entity.
    /// </summary>
    public static Pg<EntityEntry<A>> update<A>(A entity) where A : class =>
        from s in set<A>()
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
        select unit;

    /// <summary>
    /// Delete an entity.
    /// </summary>
    public static Pg<EntityEntry<A>> delete<A>(A entity) where A : class =>
        from s in set<A>()
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
        select n;

    /// <summary>
    /// Execute raw SQL with parameters.
    /// </summary>
    public static Pg<int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO<int>(io => f.ExecuteSqlRawAsync(sql, @params.ToArray(), io.Token))
        select n;

    // ==================== Transaction Management ====================

    /// <summary>
    /// Get the current transaction from EF Core (if any).
    /// </summary>
    public static Pg<Option<IDbContextTransaction>> currentTransaction =>
        from c in context
        select Optional(c.Database.CurrentTransaction);

    /// <summary>
    /// Begin a new transaction with optional isolation level.
    /// EF Core tracks the transaction automatically via Database.CurrentTransaction.
    /// </summary>
    public static Pg<IDbContextTransaction> beginTransaction(Option<IsolationLevel> level = default) =>
        from e in env
        from t in liftIO<IDbContextTransaction>(io =>
            e.Context.Database.BeginTransactionAsync((level | e.DefaultIsolation).IfNone(IsolationLevel.Unspecified), io.Token))
        select t;

    /// <summary>
    /// Commit the current transaction.
    /// </summary>
    public static Pg<Unit> commit =>
        from c in context
        from _ in liftIO<Unit>(async io =>
        {
            if (c.Database.CurrentTransaction is { } txn)
                await txn.CommitAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    public static Pg<Unit> rollback =>
        from c in context
        from _ in liftIO<Unit>(async io =>
        {
            if (c.Database.CurrentTransaction is { } txn)
                await txn.RollbackAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Execute an operation within a transaction with automatic commit/rollback.
    /// Commits on success, rolls back on any error.
    /// Uses standard MonadUnliftIO.MapIO + IO.Catch pattern.
    /// </summary>
    public static Pg<A> transact<A>(Pg<A> operation, Option<IsolationLevel> level = default) =>
        from tx in beginTransaction(level)
        from operationIO in Pg.ToIO(
            from r in operation
            from _ in liftIO<Unit>(IO.liftAsync<Unit>(async envIO =>
            {
                await tx.CommitAsync(envIO.Token);
                return unit;
            }))
            select r
        ).As()
        from result in liftIO(
            operationIO.Catch(
                _ => true,
                err => IO.liftAsync<Unit>(async envIO =>
                {
                    await tx.RollbackAsync(envIO.Token);
                    return unit;
                }).Bind(_ => IO.fail<A>(err))
            )
        )
        select result;

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
    /// Releases lock on completion or error.
    /// Uses standard MonadUnliftIO.ToIO + IO.Catch pattern.
    /// </summary>
    public static Pg<A> withAdvisoryLock<A>(long key, Pg<A> operation) =>
        from e in env
        from _ in advisoryLock(key)
        from operationIO in Pg.ToIO(operation).As()
        from result in liftIO(
            operationIO.Catch(
                _ => true,
                err => advisoryUnlockIO(key, e.Connection)
                         .Bind(_ => IO.fail<A>(err))
            )
        )
        from __ in liftIO(advisoryUnlockIO(key, e.Connection))
        select result;

    /// <summary>
    /// Release an advisory lock using a captured connection (pure IO).
    /// </summary>
    static IO<Unit> advisoryUnlockIO(long key, NpgsqlConnection conn) =>
        IO.liftAsync<Unit>(async envIO =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT pg_advisory_unlock({key})";
            await cmd.ExecuteScalarAsync(envIO.Token);
            return unit;
        });

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
            return toSeq(list).Strict();
        })
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
        select result;

    // ==================== Npgsql-Specific: JSONB ====================

    /// <summary>
    /// Query JSONB data using jsonb_path_query_first.
    /// </summary>
    public static Pg<Option<A>> jsonbPath<A>(
        string table,
        string jsonColumn,
        string jsonPath,
        Option<object> vars = default) =>
        from e in env
        from result in liftIO<Option<A>>(async io =>
        {
            if (e.Connection.State != ConnectionState.Open)
                await e.Connection.OpenAsync(io.Token);
            await using var cmd = e.Connection.CreateCommand();
            cmd.CommandText = vars.IsNone
                ? $"SELECT jsonb_path_query_first({jsonColumn}, $1) FROM {table}"
                : $"SELECT jsonb_path_query_first({jsonColumn}, $1, $2) FROM {table}";
            cmd.Parameters.AddWithValue(jsonPath);
            vars.IfSome(v => cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, v));
            var scalar = await cmd.ExecuteScalarAsync(io.Token);
            return scalar == null || scalar == DBNull.Value
                ? Option<A>.None
                : Some(JsonConvert.DeserializeObject<A>(scalar.ToString()!)!);
        })
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
