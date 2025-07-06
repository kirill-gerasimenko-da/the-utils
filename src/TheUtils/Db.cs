namespace TheUtils;

using System.Data;
using LanguageExt;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static LanguageExt.Prelude;

public abstract record Db
{
    public record EfCore(DbContext Context) : Db;
}

public static class Db<M, RT>
    where RT : Has<M, Db>
    where M : MonadIO<M>, Fallible<M>
{
    public static K<M, DbContext> context => Has<M, RT, Db>.ask.Map(c => ((Db.EfCore)c).Context);

    public static K<M, DatabaseFacade> facade => context.Map(c => c.Database);

    #region seq
    public static K<M, Seq<A>> seq<A>(IQueryable<A> query) =>
        context.Bind(_ => liftIO(io => query.ToListAsync(io.Token)).Map(toSeq));

    public static K<M, Seq<A>> seq<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in seq(q)
        select r;

    public static K<M, Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in seq(q)
        select r;
    #endregion

    #region any
    public static K<M, bool> any<A>(IQueryable<A> query) =>
        context.Bind(_ => liftIO(rt => query.AnyAsync(rt.Token)));

    public static K<M, bool> any<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in any(q)
        select r;

    public static K<M, bool> any<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in any(q)
        select r;
    #endregion

    #region count
    public static K<M, int> count<A>(IQueryable<A> query) =>
        context.Bind(_ => liftIO(rt => query.CountAsync(rt.Token)));

    public static K<M, int> count<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in count(q)
        select r;

    public static K<M, int> count<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in count(q)
        select r;
    #endregion

    #region head
    public static K<M, Option<A>> head<A>(IQueryable<A> query) =>
        context.Bind(_ => liftIO(rt => query.FirstOrDefaultAsync(rt.Token)).Map(Optional));

    public static K<M, Option<A>> head<A>(IQueryable<A?> query)
        where A : struct =>
        context.Bind(_ => liftIO(rt => query.FirstOrDefaultAsync(rt.Token)).Map(Optional));

    public static K<M, Option<A>> head<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in head(q)
        select r;

    public static K<M, Option<A>> headNullable<A>(FormattableString sql)
        where A : struct => from q in query<A?>(sql) from r in head(q) select r.Bind(Optional);

    public static K<M, Option<A>> head<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in head(q)
        select r;

    public static K<M, Option<A>> headNullable<A>(string sql, Seq<object> @params = default)
        where A : struct =>
        from q in query<A?>(sql, @params)
        from r in head(q)
        select r.Bind(Optional);
    #endregion

    #region headT
    public static OptionT<M, A> headT<A>(IQueryable<A> query) =>
        liftIO(rt => query.FirstOrDefaultAsync(rt.Token)).Map(Optional);

    public static OptionT<M, A> headT<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in headT(q)
        select r;

    public static OptionT<M, A> headT<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in headT(q)
        select r;
    #endregion

    #region single
    public static K<M, A> single<A>(IQueryable<A> query) =>
        context.Bind(_ => liftIO(rt => query.SingleAsync(rt.Token)));

    public static K<M, A> single<A>(FormattableString sql) =>
        from q in query<A>(sql)
        from r in single(q)
        select r;

    public static K<M, A> single<A>(string sql, Seq<object> @params = default) =>
        from q in query<A>(sql, @params)
        from r in single(q)
        select r;
    #endregion

    public static K<M, DbSet<A>> set<A>()
        where A : class => context.Map(c => c.Set<A>());

    public static K<M, int> saveChanges =>
        from c in context
        from n in liftIO(async rt => await c.SaveChangesAsync(rt.Token))
        select n;

    public static K<M, int> execute(FormattableString sql) =>
        from f in facade
        from n in liftIO(async rt => await f.ExecuteSqlAsync(sql, rt.Token))
        select n;

    public static K<M, int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO(async rt => await f.ExecuteSqlRawAsync(sql, @params.ToArray(), rt.Token))
        select n;

    public static K<M, IDbContextTransaction> beginTransaction(
        IsolationLevel isolation = IsolationLevel.Unspecified
    ) =>
        from f in facade
        from t in liftIO(async rt => await f.BeginTransactionAsync(isolation, rt.Token))
        select t;

    public static K<M, Unit> commitTransaction =>
        from f in facade
        from _ in liftIO(async rt => await f.CommitTransactionAsync(rt.Token))
        select unit;

    public static K<M, Unit> rollbackTransaction =>
        from f in facade
        from _ in liftIO(async rt => await f.RollbackTransactionAsync(rt.Token))
        select unit;

    public static K<M, IQueryable<A>> query<A>(FormattableString sql) =>
        facade.Map(x => x.SqlQuery<A>(sql));

    public static K<M, IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        facade.Map(x => x.SqlQueryRaw<A>(sql, @params.ToArray()));

    public static K<M, EntityEntry<A>> add<A>(A a)
        where A : class =>
        from s in set<A>()
        from e in liftIO(async rt => await s.AddAsync(a, rt.Token))
        select e;

    public static K<M, Unit> addRange<A>(Seq<A> a)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(async rt => await s.AddRangeAsync(a, rt.Token))
        select unit;

    public static K<M, EntityEntry<A>> update<A>(A a)
        where A : class => set<A>().Map(x => x.Update(a));

    public static K<M, Unit> updateRange<A>(Seq<A> a)
        where A : class =>
        set<A>()
            .Map(x =>
            {
                x.UpdateRange(a);
                return unit;
            });

    public static K<M, EntityEntry<A>> delete<A>(A a)
        where A : class => set<A>().Map(x => x.Remove(a));

    public static K<M, Unit> deleteRange<A>(Seq<A> a)
        where A : class =>
        set<A>()
            .Map(x =>
            {
                x.RemoveRange(a);
                return unit;
            });
}
