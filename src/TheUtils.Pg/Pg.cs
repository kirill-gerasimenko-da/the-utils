namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using static LanguageExt.Prelude;

/// <summary>
/// The PostgreSQL monad - wraps ReaderT&lt;PgEnv, IO, A&gt;.
/// Provides effectful PostgreSQL database operations with environment access.
/// </summary>
public readonly record struct Pg<A>(
    ReaderT<PgEnv, IO, A> runPg
) : K<Pg, A>
{
    /// <summary>
    /// Run the computation with the provided environment.
    /// </summary>
    public IO<A> Run(PgEnv env) => runPg.Run(env).As();

    // LINQ query syntax support
    public Pg<B> Map<B>(Func<A, B> f) => new(runPg.Map(f));
    public Pg<B> Bind<B>(Func<A, Pg<B>> f) => new(runPg.Bind(a => f(a).runPg));
    public Pg<B> Select<B>(Func<A, B> f) => Map(f);
    public Pg<C> SelectMany<B, C>(Func<A, Pg<B>> bind, Func<A, B, C> project) =>
        Bind(a => bind(a).Map(b => project(a, b)));
}

/// <summary>
/// Pg witness type with trait implementations.
/// Uses Deriving for Monad; manual for MonadIO, MonadUnliftIO, Fallible, Readable.
/// </summary>
public partial class Pg :
    Deriving.Monad<Pg, ReaderT<PgEnv, IO>>
{
    // ========== Deriving Morphisms (Required) ==========

    /// <summary>
    /// Transform Pg to the underlying ReaderT transformer.
    /// </summary>
    public static K<ReaderT<PgEnv, IO>, A> Transform<A>(K<Pg, A> fa) =>
        fa.As().runPg;

    /// <summary>
    /// CoTransform from ReaderT back to Pg.
    /// </summary>
    public static K<Pg, A> CoTransform<A>(K<ReaderT<PgEnv, IO>, A> fa) =>
        new Pg<A>(fa.As());

    // ========== Convenience ==========

    /// <summary>
    /// Convert K&lt;Pg, A&gt; to Pg&lt;A&gt;.
    /// </summary>
    public static Pg<A> As<A>(K<Pg, A> ma) => (Pg<A>)ma;
}

/// <summary>
/// Manual trait implementations that cannot be derived.
/// MonadIO, MonadUnliftIO, Fallible, and Readable require explicit implementation.
/// </summary>
public partial class Pg : MonadIO<Pg>, MonadUnliftIO<Pg>, Fallible<Pg>, Readable<Pg, PgEnv>
{
    // ========== MonadIO ==========

    /// <summary>
    /// Lift an IO computation into Pg.
    /// </summary>
    public static K<Pg, A> LiftIO<A>(IO<A> io) =>
        CoTransform(MonadIO.liftIO<ReaderT<PgEnv, IO>, A>(io));

    // ========== MonadUnliftIO ==========

    /// <summary>
    /// Extract the IO from within a Pg computation.
    /// Returns a Pg that, when run, produces the IO that the original computation would produce.
    /// </summary>
    public static K<Pg, IO<A>> ToIO<A>(K<Pg, A> ma) =>
        Asks<IO<A>>(env => ma.As().Run(env));

    // ========== Fallible ==========

    /// <summary>
    /// Fail with an error.
    /// </summary>
    public static K<Pg, A> Fail<A>(Error error) =>
        LiftIO(IO.fail<A>(error));

    /// <summary>
    /// Catch errors matching the predicate and handle them.
    /// If the predicate doesn't match, the error is re-thrown.
    /// </summary>
    public static K<Pg, A> Catch<A>(
        K<Pg, A> ma,
        Func<Error, bool> predicate,
        Func<Error, K<Pg, A>> handler)
    {
        return new Pg<A>(
            new ReaderT<PgEnv, IO, A>(env =>
                ma.As().Run(env).Catch(err =>
                    predicate(err)
                        ? handler(err).As().Run(env)
                        : IO.fail<A>(err))));
    }

    // ========== Readable ==========

    /// <summary>
    /// Access the environment via a projection function.
    /// </summary>
    public static K<Pg, A> Asks<A>(Func<PgEnv, A> f) =>
        new Pg<A>(Readable.asks<ReaderT<PgEnv, IO>, PgEnv, A>(f).As());

    /// <summary>
    /// Run a computation with a locally modified environment.
    /// </summary>
    public static K<Pg, A> Local<A>(Func<PgEnv, PgEnv> f, K<Pg, A> ma)
    {
        return new Pg<A>(Readable.local(f, ma.As().runPg).As());
    }
}

/// <summary>
/// Extension methods for Pg monad.
/// </summary>
public static class PgExtensions
{
    /// <summary>
    /// Convert K&lt;Pg, A&gt; to Pg&lt;A&gt;.
    /// </summary>
    public static Pg<A> As<A>(this K<Pg, A> ma) => Pg.As(ma);

    /// <summary>
    /// Ignore the result, returning Unit.
    /// </summary>
    public static Pg<Unit> Ignore<A>(this Pg<A> ma) => ma.Map(_ => unit);
}
