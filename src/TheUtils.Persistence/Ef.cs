namespace TheUtils.Persistence;

using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using static LanguageExt.Prelude;

public static class Ef
{
    public interface HasDbContext
    {
        DbContext DbContext { get; }
    }

    public static Eff<Env, Seq<A>> seq<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => toSeq(await query.ToListAsync(rt.Token)));

    public static Eff<Env, bool> any<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => await query.AnyAsync(rt.Token));

    public static Eff<Env, Option<A>> head<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => Optional(await query.FirstOrDefaultAsync(rt.Token)));

    public static OptionT<Eff<Env>, A> headT<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => Optional(await query.FirstOrDefaultAsync(rt.Token)));

    public static Eff<Env, DbSet<A>> set<Env, A>()
        where Env : HasDbContext
        where A : class => liftEff<Env, DbSet<A>>(rt => rt.DbContext.Set<A>());

    public static Eff<Env, DbContext> ctx<Env>()
        where Env : HasDbContext => liftEff<Env, DbContext>(rt => rt.DbContext);

    public static Eff<Env, int> saveChanges<Env>()
        where Env : HasDbContext =>
        from ctx in ctx<Env>()
        from count in liftEff(async rt => await ctx.SaveChangesAsync(rt.EnvIO.Token))
        select count;

    public static Eff<Env, DatabaseFacade> facade<Env>()
        where Env : HasDbContext => liftEff<Env, DatabaseFacade>(rt => rt.DbContext.Database);

    public static Eff<Env, EntityEntry<A>> add<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from entry in liftIO(async rt => await items.AddAsync(a, rt.Token))
        select entry;

    public static Eff<Env, Unit> addRange<Env, A>(Seq<A> a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftIO(async rt => await items.AddRangeAsync(a, rt.Token))
        select unit;

    public static Eff<Env, EntityEntry<A>> update<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from entry in liftEff(() => items.Update(a))
        select entry;

    public static Eff<Env, Unit> updateRange<Env, A>(Seq<A> a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftEff(() => items.UpdateRange(a))
        select unit;

    public static Eff<Env, EntityEntry<A>> delete<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from entry in liftEff(() => items.Remove(a))
        select entry;

    public static Eff<Env, Unit> deleteRange<Env, A>(Seq<A> a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftEff(() => items.RemoveRange(a))
        select unit;
}