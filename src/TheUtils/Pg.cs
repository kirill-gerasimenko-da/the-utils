namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using static LanguageExt.Prelude;

/// <summary>
/// The PostgreSQL monad - wraps StateT&lt;PgState, ReaderT&lt;PgEnv, IO&gt;, A&gt;.
/// Provides stateful, effectful PostgreSQL database operations with automatic transaction tracking.
/// </summary>
public readonly record struct Pg<A>(
    StateT<PgState, ReaderT<PgEnv, IO>, A> runPg
) : K<Pg, A>
{
    /// <summary>
    /// Run the computation with explicit environment and initial state.
    /// </summary>
    public IO<(A Value, PgState State)> Run(PgEnv env, PgState state) =>
        runPg.Run(state).Run(env).As();

    /// <summary>
    /// Run the computation with default initial state.
    /// </summary>
    public IO<(A Value, PgState State)> Run(PgEnv env) =>
        Run(env, PgState.Initial);

    /// <summary>
    /// Run the computation and discard the final state.
    /// </summary>
    public IO<A> RunUnit(PgEnv env) =>
        Run(env).Map(t => t.Value);

    /// <summary>
    /// Run the computation and return only the final state.
    /// </summary>
    public IO<PgState> RunState(PgEnv env) =>
        Run(env).Map(t => t.State);

    // LINQ query syntax support
    public Pg<B> Map<B>(Func<A, B> f) => new(runPg.Map(f));
    public Pg<B> Bind<B>(Func<A, Pg<B>> f) => new(runPg.Bind(a => f(a).runPg));
    public Pg<B> Select<B>(Func<A, B> f) => Map(f);
    public Pg<C> SelectMany<B, C>(Func<A, Pg<B>> bind, Func<A, B, C> project) =>
        Bind(a => bind(a).Map(b => project(a, b)));
}

/// <summary>
/// Pg witness type with trait implementations.
/// Uses Deriving for Monad and Stateful; manual for MonadIO, Fallible, Readable.
/// </summary>
public partial class Pg :
    Deriving.Monad<Pg, StateT<PgState, ReaderT<PgEnv, IO>>>,
    Deriving.Stateful<Pg, StateT<PgState, ReaderT<PgEnv, IO>>, PgState>
{
    // ========== Deriving Morphisms (Required) ==========

    /// <summary>
    /// Transform Pg to the underlying StateT transformer.
    /// </summary>
    public static K<StateT<PgState, ReaderT<PgEnv, IO>>, A> Transform<A>(K<Pg, A> fa) =>
        fa.As().runPg;

    /// <summary>
    /// CoTransform from StateT back to Pg.
    /// </summary>
    public static K<Pg, A> CoTransform<A>(K<StateT<PgState, ReaderT<PgEnv, IO>>, A> fa) =>
        new Pg<A>(fa.As());

    // ========== Convenience ==========

    /// <summary>
    /// Convert K&lt;Pg, A&gt; to Pg&lt;A&gt;.
    /// </summary>
    public static Pg<A> As<A>(K<Pg, A> ma) => (Pg<A>)ma;
}

/// <summary>
/// Manual trait implementations that cannot be derived.
/// MonadIO, Fallible, and Readable require explicit implementation.
/// </summary>
public partial class Pg : MonadIO<Pg>, Fallible<Pg>, Readable<Pg, PgEnv>
{
    // ========== MonadIO ==========

    /// <summary>
    /// Lift an IO computation into Pg.
    /// </summary>
    public static K<Pg, A> LiftIO<A>(IO<A> io) =>
        CoTransform(MonadIO.liftIO<StateT<PgState, ReaderT<PgEnv, IO>>, A>(io));

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
            new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
                new ReaderT<PgEnv, IO, (A, PgState)>(env =>
                    ma.As().Run(env, state).Catch(err =>
                        predicate(err)
                            ? handler(err).As().Run(env, state)
                            : IO.fail<(A, PgState)>(err)))));
    }

    // ========== Readable ==========

    /// <summary>
    /// Access the environment via a projection function.
    /// </summary>
    public static K<Pg, A> Asks<A>(Func<PgEnv, A> f) =>
        new Pg<A>(new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
            Readable.asks<ReaderT<PgEnv, IO>, PgEnv, (A, PgState)>(env => (f(env), state))));

    /// <summary>
    /// Run a computation with a locally modified environment.
    /// </summary>
    public static K<Pg, A> Local<A>(Func<PgEnv, PgEnv> f, K<Pg, A> ma)
    {
        return new Pg<A>(
            new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
                Readable.local(f, ma.As().runPg.Run(state))));
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
