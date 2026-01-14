namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using static LanguageExt.Prelude;

/// <summary>
/// The database monad - wraps ReaderT&lt;DbEnv, IO, A&gt;.
/// Provides effectful database operations with environment access.
/// </summary>
public readonly record struct Db<A>(ReaderT<DbEnv, IO, A> runDb) : K<Db, A>
{
    /// <summary>
    /// Run the computation with the provided environment.
    /// </summary>
    public IO<A> Run(DbEnv env) => runDb.Run(env).As();

    // LINQ query syntax support
    public Db<B> Map<B>(Func<A, B> f) => new(runDb.Map(f));

    public Db<B> Bind<B>(Func<A, Db<B>> f) => new(runDb.Bind(a => f(a).runDb));

    public Db<B> Select<B>(Func<A, B> f) => Map(f);

    public Db<C> SelectMany<B, C>(Func<A, Db<B>> bind, Func<A, B, C> project) =>
        Bind(a => bind(a).Map(b => project(a, b)));
}

/// <summary>
/// Db witness type with trait implementations.
/// Uses Deriving for Monad; manual for MonadIO, MonadUnliftIO, Fallible, Readable.
/// </summary>
public partial class Db : Deriving.Monad<Db, ReaderT<DbEnv, IO>>
{
    // ========== Deriving Morphisms (Required) ==========

    /// <summary>
    /// Transform Db to the underlying ReaderT transformer.
    /// </summary>
    public static K<ReaderT<DbEnv, IO>, A> Transform<A>(K<Db, A> fa) => fa.As().runDb;

    /// <summary>
    /// CoTransform from ReaderT back to Db.
    /// </summary>
    public static K<Db, A> CoTransform<A>(K<ReaderT<DbEnv, IO>, A> fa) => new Db<A>(fa.As());

    // ========== Convenience ==========

    /// <summary>
    /// Convert K&lt;Db, A&gt; to Db&lt;A&gt;.
    /// </summary>
    public static Db<A> As<A>(K<Db, A> ma) => (Db<A>)ma;
}

/// <summary>
/// Manual trait implementations that cannot be derived.
/// MonadIO, MonadUnliftIO, Fallible, and Readable require explicit implementation.
/// </summary>
public partial class Db : MonadIO<Db>, MonadUnliftIO<Db>, Fallible<Db>, Readable<Db, DbEnv>
{
    // ========== MonadIO ==========

    /// <summary>
    /// Lift an IO computation into Db.
    /// </summary>
    public static K<Db, A> LiftIO<A>(IO<A> io) =>
        CoTransform(MonadIO.liftIO<ReaderT<DbEnv, IO>, A>(io));

    // ========== MonadUnliftIO ==========

    /// <summary>
    /// Extract the IO from within a Db computation.
    /// Returns a Db that, when run, produces the IO that the original computation would produce.
    /// </summary>
    public static K<Db, IO<A>> ToIO<A>(K<Db, A> ma) => Asks<IO<A>>(env => ma.As().Run(env));

    // ========== Fallible ==========

    /// <summary>
    /// Fail with an error.
    /// </summary>
    public static K<Db, A> Fail<A>(Error error) => LiftIO(IO.fail<A>(error));

    /// <summary>
    /// Catch errors matching the predicate and handle them.
    /// If the predicate doesn't match, the error is re-thrown.
    /// </summary>
    public static K<Db, A> Catch<A>(
        K<Db, A> ma,
        Func<Error, bool> predicate,
        Func<Error, K<Db, A>> handler
    )
    {
        return new Db<A>(
            new ReaderT<DbEnv, IO, A>(env =>
                ma.As()
                    .Run(env)
                    .Catch(err => predicate(err) ? handler(err).As().Run(env) : IO.fail<A>(err))
            )
        );
    }

    // ========== Readable ==========

    /// <summary>
    /// Access the environment via a projection function.
    /// </summary>
    public static K<Db, A> Asks<A>(Func<DbEnv, A> f) =>
        new Db<A>(Readable.asks<ReaderT<DbEnv, IO>, DbEnv, A>(f).As());

    /// <summary>
    /// Run a computation with a locally modified environment.
    /// </summary>
    public static K<Db, A> Local<A>(Func<DbEnv, DbEnv> f, K<Db, A> ma)
    {
        return new Db<A>(Readable.local(f, ma.As().runDb).As());
    }
}

/// <summary>
/// Extension methods for Db monad.
/// </summary>
public static class DbExtensions
{
    /// <summary>
    /// Convert K&lt;Db, A&gt; to Db&lt;A&gt;.
    /// </summary>
    public static Db<A> As<A>(this K<Db, A> ma) => Db.As(ma);

    /// <summary>
    /// Ignore the result, returning Unit.
    /// </summary>
    public static Db<Unit> Ignore<A>(this Db<A> ma) => ma.Map(_ => unit);
}
