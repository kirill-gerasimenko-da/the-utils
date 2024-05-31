namespace TheUtils.Persistence;

using LanguageExt;

public interface Storage<Env, A, in Id>
    where Env : HasDbContext
{
    public static abstract OptionT<Eff<Env>, A> find(Id id);
    public static abstract Eff<Env, Unit> add(A a);
    public static abstract Eff<Env, Unit> update(A a);
    public static abstract Eff<Env, Unit> delete(A a);
}
