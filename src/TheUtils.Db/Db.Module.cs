namespace TheUtils;

using System.Data;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static LanguageExt.Prelude;

/// <summary>
/// Database monad operations - queries, transactions, and EF Core operations.
/// </summary>
public partial class Db
{
    // ==================== Environment Access ====================

    /// <summary>
    /// Access the current environment.
    /// </summary>
    public static Db<DbEnv> env => Asks(identity).As();

    /// <summary>
    /// Access the DbContext from environment.
    /// </summary>
    public static Db<DbContext> context => from e in env select e.Context;

    /// <summary>
    /// Access the DatabaseFacade for raw operations.
    /// </summary>
    public static Db<DatabaseFacade> facade => from c in context select c.Database;

    // ==================== IO Lifting ====================

    /// <summary>
    /// Lift an IO operation into Db.
    /// </summary>
    public static Db<A> liftIO<A>(IO<A> io) => LiftIO(io).As();

    /// <summary>
    /// Lift an async operation into Db.
    /// </summary>
    public static Db<A> liftIO<A>(Func<EnvIO, Task<A>> f) => liftIO(IO.liftAsync(f));

    /// <summary>
    /// Lift a synchronous operation into Db.
    /// </summary>
    public static Db<A> liftIO<A>(Func<A> f) => liftIO(IO.lift(f));

    // ==================== Pure & Fail ====================

    /// <summary>
    /// Lift a pure value into Db.
    /// </summary>
    public static Db<A> pure<A>(A value) => Applicative.pure<Db, A>(value).As();

    /// <summary>
    /// Fail with an error.
    /// </summary>
    public static Db<A> fail<A>(Error error) => Fail<A>(error).As();

    /// <summary>
    /// Fail with a string message.
    /// </summary>
    public static Db<A> fail<A>(string message) => fail<A>(Error.New(message));

    // ==================== Query Operations (EF Core) ====================

    /// <summary>
    /// Execute a LINQ query and return results as Seq.
    /// </summary>
    public static Db<Seq<A>> seq<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<List<A>>(io => query.ToListAsync(io.Token))
        select toSeq(r).Strict();

    /// <summary>
    /// Execute an interpolated SQL query and return results as Seq.
    /// </summary>
    public static Db<Seq<A>> seq<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in seq(q)
        select r;

    /// <summary>
    /// Execute a raw SQL query with parameters and return results as Seq.
    /// </summary>
    public static Db<Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in seq(q)
        select r;

    /// <summary>
    /// Check if any rows match the query.
    /// </summary>
    public static Db<bool> any<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(io => query.AnyAsync(io.Token))
        select r;

    /// <summary>
    /// Check if any rows match the interpolated SQL.
    /// </summary>
    public static Db<bool> any<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in any(q)
        select r;

    /// <summary>
    /// Count rows matching the query.
    /// </summary>
    public static Db<int> count<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(io => query.CountAsync(io.Token))
        select r;

    /// <summary>
    /// Count rows matching the interpolated SQL.
    /// </summary>
    public static Db<int> count<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in count(q)
        select r;

    /// <summary>
    /// Get the first row or None.
    /// </summary>
    public static Db<Option<A>> head<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.FirstOrDefaultAsync(io.Token)!)
        select Optional(r);

    /// <summary>
    /// Get the first row from interpolated SQL or None.
    /// </summary>
    public static Db<Option<A>> head<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in head(q)
        select r;

    // ==================== headT (OptionT variant) ====================

    /// <summary>
    /// Get the first row as OptionT (for monad transformer chaining).
    /// </summary>
    public static OptionT<Db, A> headT<A>(IQueryable<A> query) => OptionT.lift(head(query));

    /// <summary>
    /// Get the first row from interpolated SQL as OptionT.
    /// </summary>
    public static OptionT<Db, A> headT<A>(FormattableString sql)
        where A : class => OptionT.lift(head<A>(sql));

    /// <summary>
    /// Get exactly one row (throws if not exactly one).
    /// </summary>
    public static Db<A> single<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.SingleAsync(io.Token))
        select r;

    /// <summary>
    /// Get exactly one row from interpolated SQL.
    /// </summary>
    public static Db<A> single<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in single(q)
        select r;

    /// <summary>
    /// Get a DbSet for the entity type.
    /// </summary>
    public static Db<DbSet<A>> set<A>()
        where A : class => from c in context select c.Set<A>();

    /// <summary>
    /// Create a query from interpolated SQL.
    /// </summary>
    public static Db<IQueryable<A>> query<A>(FormattableString sql) =>
        from f in facade
        select f.SqlQuery<A>(sql);

    /// <summary>
    /// Create a query from raw SQL with parameters.
    /// </summary>
    public static Db<IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        from f in facade
        select f.SqlQueryRaw<A>(sql, @params.ToArray());

    // ==================== Entity Operations ====================

    /// <summary>
    /// Add an entity to the context.
    /// </summary>
    public static Db<EntityEntry<A>> add<A>(A entity)
        where A : class =>
        from s in set<A>()
        from e in liftIO<EntityEntry<A>>(io => s.AddAsync(entity, io.Token).AsTask())
        select e;

    /// <summary>
    /// Add multiple entities.
    /// </summary>
    public static Db<Unit> addRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(async io =>
        {
            await s.AddRangeAsync(entities, io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Update an entity.
    /// </summary>
    public static Db<EntityEntry<A>> update<A>(A entity)
        where A : class => from s in set<A>() select s.Update(entity);

    /// <summary>
    /// Update multiple entities.
    /// </summary>
    public static Db<Unit> updateRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(() =>
        {
            s.UpdateRange(entities);
            return unit;
        })
        select unit;

    /// <summary>
    /// Delete an entity.
    /// </summary>
    public static Db<EntityEntry<A>> delete<A>(A entity)
        where A : class => from s in set<A>() select s.Remove(entity);

    /// <summary>
    /// Delete multiple entities.
    /// </summary>
    public static Db<Unit> deleteRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(() =>
        {
            s.RemoveRange(entities);
            return unit;
        })
        select unit;

    /// <summary>
    /// Save all pending changes.
    /// </summary>
    public static Db<int> saveChanges =>
        from c in context
        from n in liftIO(io => c.SaveChangesAsync(io.Token))
        select n;

    // ==================== Raw SQL Execution ====================

    /// <summary>
    /// Execute interpolated SQL (INSERT, UPDATE, DELETE).
    /// </summary>
    public static Db<int> execute(FormattableString sql) =>
        from f in facade
        from n in liftIO(io => f.ExecuteSqlAsync(sql, io.Token))
        select n;

    /// <summary>
    /// Execute raw SQL with parameters.
    /// </summary>
    public static Db<int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO(io => f.ExecuteSqlRawAsync(sql, @params.ToArray(), io.Token))
        select n;

    // ==================== Transaction Management ====================

    /// <summary>
    /// Get the current transaction from EF Core (if any).
    /// </summary>
    public static Db<Option<IDbContextTransaction>> currentTransaction =>
        from c in context
        select Optional(c.Database.CurrentTransaction);

    /// <summary>
    /// Begin a new transaction with optional isolation level.
    /// EF Core tracks the transaction automatically via Database.CurrentTransaction.
    /// </summary>
    public static Db<IDbContextTransaction> beginTransaction(
        Option<IsolationLevel> level = default
    ) =>
        from e in env
        from t in liftIO(io =>
            e.Context.Database.BeginTransactionAsync(
                (level | e.DefaultIsolation).IfNone(IsolationLevel.Unspecified),
                io.Token
            )
        )
        select t;

    /// <summary>
    /// Commit the current transaction.
    /// </summary>
    public static Db<Unit> commit =>
        from c in context
        from _ in liftIO(async io =>
        {
            if (c.Database.CurrentTransaction is { } txn)
                await txn.CommitAsync(io.Token);
            return unit;
        })
        select unit;

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    public static Db<Unit> rollback =>
        from c in context
        from _ in liftIO(async io =>
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
    public static Db<A> transact<A>(Db<A> operation, Option<IsolationLevel> level = default) =>
        from tx in beginTransaction(level)
        from operationIO in ToIO(
                from r in operation
                from _ in liftIO(
                    IO.liftAsync(async envIO =>
                    {
                        await tx.CommitAsync(envIO.Token);
                        return unit;
                    })
                )
                select r
            )
            .As()
        from result in liftIO(
            operationIO.Catch(
                _ => true,
                err =>
                    IO.liftAsync(async envIO =>
                        {
                            await tx.RollbackAsync(envIO.Token);
                            return unit;
                        })
                        .Bind(_ => IO.fail<A>(err))
            )
        )
        select result;
}
