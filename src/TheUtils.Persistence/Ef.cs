namespace TheUtils.Persistence;

using LanguageExt;
using Microsoft.EntityFrameworkCore;
using static LanguageExt.Prelude;

public static class Ef
{
    public static Eff<Env, Seq<A>> all<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => toSeq(await query.ToListAsync(rt.Token)));

    public static Eff<Env, Option<A>> head<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => Optional(await query.FirstOrDefaultAsync(rt.Token)));

    public static OptionT<Eff<Env>, A> headT<Env, A>(IQueryable<A> query) =>
        liftIO(async rt => Optional(await query.FirstOrDefaultAsync(rt.Token)));

    public static Eff<Env, DbSet<A>> set<Env, A>()
        where Env : HasDbContext
        where A : class => liftEff<Env, DbSet<A>>(rt => rt.DbContext.Set<A>());

    public static Eff<Env, Unit> add<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftIO(async rt => await items.AddAsync(a, rt.Token))
        select unit;

    public static Eff<Env, Unit> update<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftEff(() => items.Update(a))
        select unit;

    public static Eff<Env, Unit> delete<Env, A>(A a)
        where Env : HasDbContext
        where A : class =>
        from items in set<Env, A>()
        from _ in liftEff(() => items.Remove(a))
        select unit;
}