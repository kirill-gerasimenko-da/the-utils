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
/// Read-only environment for database monad operations.
/// Contains connection configuration and defaults.
/// </summary>
public record DbRT(
    DbContext Context,
    Option<IsolationLevel> DefaultIsolation = default,
    Option<TimeSpan> CommandTimeout = default
)
{
    /// <summary>
    /// Creates environment from just a DbContext (most common case).
    /// </summary>
    public static DbRT FromContext(DbContext context) => new(context);
}

/// <summary>
/// Database monad operations module.
/// All methods return Eff&lt;DbRT, A&gt; — LanguageExt's built-in reader + IO monad.
/// </summary>
public static class Db
{
    // ==================== Runtime Access ====================

    public static Eff<DbRT, DbRT> runtime => Eff.runtime<DbRT>();

    public static Eff<DbRT, DbContext> context => from rt in runtime select rt.Context;

    public static Eff<DbRT, DatabaseFacade> facade => from c in context select c.Database;

    // ==================== IO Lifting ====================

    public static Eff<DbRT, A> liftIO<A>(IO<A> io) => Eff.lift<DbRT, A>(io);

    public static Eff<DbRT, A> liftIO<A>(Func<EnvIO, Task<A>> f) => liftIO(IO.liftAsync(f));

    public static Eff<DbRT, A> liftIO<A>(Func<A> f) => liftIO(IO.lift(f));

    // ==================== Pure & Fail ====================

    public static Eff<DbRT, A> pure<A>(A value) => Eff.Success<DbRT, A>(value);

    public static Eff<DbRT, A> fail<A>(Error error) => Eff.Fail<DbRT, A>(error);

    public static Eff<DbRT, A> fail<A>(string message) => fail<A>(Error.New(message));

    // ==================== Query Operations (EF Core) ====================

    public static Eff<DbRT, Seq<A>> seq<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<List<A>>(io => query.ToListAsync(io.Token))
        select toSeq(r).Strict();

    public static Eff<DbRT, Seq<A>> seq<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in seq(q)
        select r;

    public static Eff<DbRT, Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in seq(q)
        select r;

    public static Eff<DbRT, bool> any<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(io => query.AnyAsync(io.Token))
        select r;

    public static Eff<DbRT, bool> any<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in any(q)
        select r;

    public static Eff<DbRT, int> count<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(io => query.CountAsync(io.Token))
        select r;

    public static Eff<DbRT, int> count<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in count(q)
        select r;

    public static Eff<DbRT, Option<A>> head<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.FirstOrDefaultAsync(io.Token)!)
        select Optional(r);

    public static Eff<DbRT, Option<A>> head<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in head(q)
        select r;

    // ==================== headT (OptionT variant) ====================

    public static OptionT<Eff<DbRT>, A> headT<A>(IQueryable<A> query) => OptionT.lift(head(query));

    public static OptionT<Eff<DbRT>, A> headT<A>(FormattableString sql)
        where A : class => OptionT.lift(head<A>(sql));

    public static Eff<DbRT, A> single<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO<A>(io => query.SingleAsync(io.Token))
        select r;

    public static Eff<DbRT, A> single<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in single(q)
        select r;

    public static Eff<DbRT, DbSet<A>> set<A>()
        where A : class => from c in context select c.Set<A>();

    public static Eff<DbRT, IQueryable<A>> query<A>(FormattableString sql) =>
        from f in facade
        select f.SqlQuery<A>(sql);

    public static Eff<DbRT, IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        from f in facade
        select f.SqlQueryRaw<A>(sql, @params.ToArray());

    // ==================== Entity Operations ====================

    public static Eff<DbRT, EntityEntry<A>> add<A>(A entity)
        where A : class =>
        from s in set<A>()
        from e in liftIO<EntityEntry<A>>(io => s.AddAsync(entity, io.Token).AsTask())
        select e;

    public static Eff<DbRT, Unit> addRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(async io =>
        {
            await s.AddRangeAsync(entities, io.Token);
            return unit;
        })
        select unit;

    public static Eff<DbRT, EntityEntry<A>> update<A>(A entity)
        where A : class => from s in set<A>() select s.Update(entity);

    public static Eff<DbRT, Unit> updateRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(() =>
        {
            s.UpdateRange(entities);
            return unit;
        })
        select unit;

    public static Eff<DbRT, EntityEntry<A>> delete<A>(A entity)
        where A : class => from s in set<A>() select s.Remove(entity);

    public static Eff<DbRT, Unit> deleteRange<A>(Seq<A> entities)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(() =>
        {
            s.RemoveRange(entities);
            return unit;
        })
        select unit;

    public static Eff<DbRT, int> saveChanges =>
        from c in context
        from n in liftIO(io => c.SaveChangesAsync(io.Token))
        select n;

    // ==================== Raw SQL Execution ====================

    public static Eff<DbRT, int> execute(FormattableString sql) =>
        from f in facade
        from n in liftIO(io => f.ExecuteSqlAsync(sql, io.Token))
        select n;

    public static Eff<DbRT, int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO(io => f.ExecuteSqlRawAsync(sql, @params.ToArray(), io.Token))
        select n;

    // ==================== Transaction Management ====================

    public static Eff<DbRT, Option<IDbContextTransaction>> currentTransaction =>
        from c in context
        select Optional(c.Database.CurrentTransaction);

    public static Eff<DbRT, IDbContextTransaction> beginTransaction(
        Option<IsolationLevel> level = default
    ) =>
        from rt in runtime
        from t in liftIO(io =>
            rt.Context.Database.BeginTransactionAsync(
                (level | rt.DefaultIsolation).IfNone(IsolationLevel.Unspecified),
                io.Token
            )
        )
        select t;

    public static Eff<DbRT, Unit> commit =>
        from c in context
        from _ in liftIO(async io =>
        {
            if (c.Database.CurrentTransaction is { } txn)
                await txn.CommitAsync(io.Token);
            return unit;
        })
        select unit;

    public static Eff<DbRT, Unit> rollback =>
        from c in context
        from _ in liftIO(async io =>
        {
            if (c.Database.CurrentTransaction is { } txn)
                await txn.RollbackAsync(io.Token);
            return unit;
        })
        select unit;

    public static Eff<DbRT, A> transact<A>(
        Eff<DbRT, A> operation,
        Option<IsolationLevel> level = default
    ) =>
        from tx in beginTransaction(level)
        from operationIO in MonadUnliftIO
            .toIO<Eff<DbRT>, A>(
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
