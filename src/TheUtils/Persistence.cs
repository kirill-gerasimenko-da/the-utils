namespace TheUtils;

using System.Data;
using LanguageExt;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static LanguageExt.Prelude;

public static class Persistence<RT>
    where RT : Has<Eff<RT>, DbContext>
{
    public static Eff<RT, DbContext> context => RT.Ask.As();

    public static Eff<RT, DatabaseFacade> facade => context.Map(c => c.Database);

    public static Eff<RT, Seq<A>> seq<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(io => query.ToListAsync(io.Token)).Map(toSeq)
        select r;

    public static Eff<RT, bool> any<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(rt => query.AnyAsync(rt.Token))
        select r;

    public static Eff<RT, Option<A>> head<A>(IQueryable<A> query) =>
        from _ in context
        from r in liftIO(rt => query.FirstOrDefaultAsync(rt.Token)).Map(Optional)
        select r;

    public static OptionT<Eff<RT>, A> headT<A>(IQueryable<A> query) =>
        liftIO(rt => query.FirstOrDefaultAsync(rt.Token)).Map(Optional);

    public static Eff<RT, DbSet<A>> set<A>()
        where A : class => context.Map(c => c.Set<A>());

    public static Eff<RT, int> saveChanges =>
        from c in context
        from n in liftIO(async rt => await c.SaveChangesAsync(rt.Token))
        select n;

    public static Eff<RT, int> execute(FormattableString sql) =>
        from f in facade
        from n in liftIO(async rt => await f.ExecuteSqlAsync(sql, rt.Token))
        select n;

    public static Eff<RT, int> executeRaw(string sql, Seq<object> @params = default) =>
        from f in facade
        from n in liftIO(async rt => await f.ExecuteSqlRawAsync(sql, @params.ToArray(), rt.Token))
        select n;

    public static Eff<RT, IDbContextTransaction> beginTransaction(
        IsolationLevel isolation = IsolationLevel.Unspecified
    ) =>
        from f in facade
        from t in liftIO(async rt => await f.BeginTransactionAsync(isolation, rt.Token))
        select t;

    public static Eff<RT, Unit> commitTransaction =>
        from f in facade
        from _ in liftIO(async rt => await f.CommitTransactionAsync(rt.Token))
        select unit;

    public static Eff<RT, Unit> rollbackTransaction =>
        from f in facade
        from _ in liftIO(async rt => await f.RollbackTransactionAsync(rt.Token))
        select unit;

    public static Eff<RT, IQueryable<A>> query<A>(FormattableString sql) =>
        facade.Map(x => x.SqlQuery<A>(sql));

    public static Eff<RT, IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        facade.Map(x => x.SqlQueryRaw<A>(sql, @params.ToArray()));

    public static Eff<RT, EntityEntry<A>> add<A>(A a)
        where A : class =>
        from s in set<A>()
        from e in liftIO(async rt => await s.AddAsync(a, rt.Token))
        select e;

    public static Eff<RT, Unit> addRange<A>(Seq<A> a)
        where A : class =>
        from s in set<A>()
        from _ in liftIO(async rt => await s.AddRangeAsync(a, rt.Token))
        select unit;

    public static Eff<RT, EntityEntry<A>> update<A>(A a)
        where A : class => set<A>().Map(x => x.Update(a));

    public static Eff<RT, Unit> updateRange<A>(Seq<A> a)
        where A : class =>
        set<A>()
            .Map(x =>
            {
                x.UpdateRange(a);
                return unit;
            });

    public static Eff<RT, EntityEntry<A>> delete<A>(A a)
        where A : class => set<A>().Map(x => x.Remove(a));

    public static Eff<RT, Unit> deleteRange<A>(Seq<A> a)
        where A : class =>
        set<A>()
            .Map(x =>
            {
                x.RemoveRange(a);
                return unit;
            });
}