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
    where A : notnull
{
    /// <summary>
    /// Run the computation with the provided environment.
    /// </summary>
    public IO<A> Run(DbEnv env) => runDb.Run(env).As();

    // LINQ query syntax support
    public Db<B> Map<B>(Func<A, B> f)
        where B : notnull => new(runDb.Map(f));

    public Db<B> Bind<B>(Func<A, Db<B>> f)
        where B : notnull => new(runDb.Bind(a => f(a).runDb));

    public Db<B> Select<B>(Func<A, B> f)
        where B : notnull => Map(f);

    public Db<C> SelectMany<B, C>(Func<A, Db<B>> bind, Func<A, B, C> project)
        where C : notnull
        where B : notnull => Bind(a => bind(a).Map(b => project(a, b)));

    // IO interop
    public static implicit operator Db<A>(IO<A> io) => Db.LiftIO(io).As();

    public Db<C> SelectMany<B, C>(Func<A, IO<B>> bind, Func<A, B, C> project)
        where B : notnull
        where C : notnull => Bind(a => Db.LiftIO(bind(a)).As().Map(b => project(a, b)));

    // sequencing
    public static Db<A> operator >>(Db<A> lhs, Db<A> rhs) => lhs.Bind(_ => rhs);

    public static Db<A> operator >>(Db<A> lhs, IO<A> rhs) =>
        lhs.Bind(_ => Db.LiftIO(rhs).As());
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
    public static K<ReaderT<DbEnv, IO>, A> Transform<A>(K<Db, A> fa)
        where A : notnull => fa.As().runDb;

    /// <summary>
    /// CoTransform from ReaderT back to Db.
    /// </summary>
    public static K<Db, A> CoTransform<A>(K<ReaderT<DbEnv, IO>, A> fa)
        where A : notnull => new Db<A>(fa.As());

    // ========== Convenience ==========

    /// <summary>
    /// Convert K&lt;Db, A&gt; to Db&lt;A&gt;.
    /// </summary>
    public static Db<A> As<A>(K<Db, A> ma)
        where A : notnull => (Db<A>)ma;
}

/// <summary>
/// Manual trait implementations that cannot be derived.
/// MonadIO, MonadUnliftIO, Fallible, and Readable require explicit implementation.
/// </summary>
public partial class Db : MonadUnliftIO<Db>, Fallible<Db>, Readable<Db, DbEnv>
{
    // ========== MonadIO ==========

    /// <summary>
    /// Lift an IO computation into Db.
    /// </summary>
    public static K<Db, A> LiftIO<A>(IO<A> io)
        where A : notnull => CoTransform(MonadIO.liftIO<ReaderT<DbEnv, IO>, A>(io));

    // ========== MonadUnliftIO ==========

    /// <summary>
    /// Extract the IO from within a Db computation.
    /// Returns a Db that, when run, produces the IO that the original computation would produce.
    /// </summary>
    public static K<Db, IO<A>> ToIO<A>(K<Db, A> ma)
        where A : notnull => Asks<IO<A>>(e => ma.As().Run(e));

    // ========== Fallible ==========

    /// <summary>
    /// Fail with an error.
    /// </summary>
    public static K<Db, A> Fail<A>(Error error)
        where A : notnull => LiftIO(IO.fail<A>(error));

    /// <summary>
    /// Catch errors matching the predicate and handle them.
    /// If the predicate doesn't match, the error is re-thrown.
    /// </summary>
    public static K<Db, A> Catch<A>(
        K<Db, A> ma,
        Func<Error, bool> predicate,
        Func<Error, K<Db, A>> handler
    )
        where A : notnull
    {
        return new Db<A>(
            new ReaderT<DbEnv, IO, A>(e =>
                ma.As()
                    .Run(e)
                    .Catch(err => predicate(err) ? handler(err).As().Run(e) : IO.fail<A>(err))
            )
        );
    }

    // ========== Readable ==========

    /// <summary>
    /// Access the environment via a projection function.
    /// </summary>
    public static K<Db, A> Asks<A>(Func<DbEnv, A> f)
        where A : notnull => new Db<A>(Readable.asks<ReaderT<DbEnv, IO>, DbEnv, A>(f).As());

    /// <summary>
    /// Run a computation with a locally modified environment.
    /// </summary>
    public static K<Db, A> Local<A>(Func<DbEnv, DbEnv> f, K<Db, A> ma)
        where A : notnull
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
    public static Db<A> As<A>(this K<Db, A> ma)
        where A : notnull => Db.As(ma);

    /// <summary>
    /// Ignore the result, returning Unit.
    /// </summary>
    public static Db<Unit> Ignore<A>(this Db<A> ma)
        where A : notnull => ma.Map(_ => unit);
}
