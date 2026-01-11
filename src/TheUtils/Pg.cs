namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Traits;
using static LanguageExt.Prelude;

/// <summary>
/// The PostgreSQL monad - a first-class monad wrapping StateT&lt;PgState, ReaderT&lt;PgEnv, IO&gt;, A&gt;.
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

    /// <summary>
    /// Map over the result value.
    /// </summary>
    public Pg<B> Map<B>(Func<A, B> f) =>
        new(runPg.Map(f));

    /// <summary>
    /// Bind/FlatMap to chain computations.
    /// </summary>
    public Pg<B> Bind<B>(Func<A, Pg<B>> f) =>
        new(runPg.Bind(a => f(a).runPg));

    /// <summary>
    /// Select (LINQ query syntax support).
    /// </summary>
    public Pg<B> Select<B>(Func<A, B> f) => Map(f);

    /// <summary>
    /// SelectMany (LINQ query syntax support).
    /// </summary>
    public Pg<C> SelectMany<B, C>(Func<A, Pg<B>> bind, Func<A, B, C> project) =>
        Bind(a => bind(a).Map(b => project(a, b)));
}

/// <summary>
/// The Pg monad witness type and trait implementations.
/// Implements Monad, MonadIO, Fallible, Stateful, and Readable traits.
/// </summary>
public class Pg :
    Monad<Pg>,
    MonadIO<Pg>,
    Fallible<Pg>,
    Stateful<Pg, PgState>,
    Readable<Pg, PgEnv>
{
    // Type alias for the inner transformer stack
    private static StateT<PgState, ReaderT<PgEnv, IO>, A> Inner<A>(Pg<A> ma) => ma.runPg;
    private static Pg<A> Outer<A>(StateT<PgState, ReaderT<PgEnv, IO>, A> ma) => new(ma);
    private static Pg<A> Outer<A>(K<StateT<PgState, ReaderT<PgEnv, IO>>, A> ma) => new(ma.As());

    // ==================== Functor ====================

    public static K<Pg, B> Map<A, B>(Func<A, B> f, K<Pg, A> ma) =>
        Outer(Inner(ma.As()).Map(f));

    // ==================== Applicative ====================

    public static K<Pg, A> Pure<A>(A value) =>
        Outer(Applicative.pure<StateT<PgState, ReaderT<PgEnv, IO>>, A>(value));

    public static K<Pg, B> Apply<A, B>(K<Pg, Func<A, B>> mf, K<Pg, A> ma) =>
        Outer(Applicative.apply(Inner(mf.As()), Inner(ma.As())));

    public static K<Pg, B> Apply<A, B>(K<Pg, Func<A, B>> mf, Memo<Pg, A> ma) =>
        mf.Bind(f => ma.Value.Map(f));

    // ==================== Monad ====================

    public static K<Pg, B> Bind<A, B>(K<Pg, A> ma, Func<A, K<Pg, B>> f) =>
        Outer(Inner(ma.As()).Bind(a => Inner(f(a).As())));

    public static K<Pg, A> Flatten<A>(K<Pg, K<Pg, A>> mma) =>
        mma.Bind(identity);

    public static K<Pg, B> Recur<A, B>(A value, Func<A, K<Pg, Next<A, B>>> f) =>
        Outer(Monad.recur<StateT<PgState, ReaderT<PgEnv, IO>>, A, B>(value, a =>
            Inner(f(a).As())));

    // ==================== MonadIO ====================

    public static K<Pg, A> LiftIO<A>(IO<A> io) =>
        Outer(MonadIO.liftIO<StateT<PgState, ReaderT<PgEnv, IO>>, A>(io));

    // ==================== Fallible ====================

    public static K<Pg, A> Fail<A>(Error error) =>
        LiftIO(IO.fail<A>(error));

    public static K<Pg, A> Catch<A>(
        K<Pg, A> ma,
        Func<Error, bool> predicate,
        Func<Error, K<Pg, A>> handler)
    {
        return new Pg<A>(
            new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
                new ReaderT<PgEnv, IO, (A, PgState)>(env =>
                    ma.As().Run(env, state).Catch(predicate, err =>
                        handler(err).As().Run(env, state)))));
    }

    // ==================== Stateful ====================

    public static K<Pg, A> Gets<A>(Func<PgState, A> f) =>
        Outer(Stateful.gets<StateT<PgState, ReaderT<PgEnv, IO>>, PgState, A>(f));

    public static K<Pg, Unit> Put(PgState state) =>
        Outer(Stateful.put<StateT<PgState, ReaderT<PgEnv, IO>>, PgState>(state));

    public static K<Pg, Unit> Modify(Func<PgState, PgState> f) =>
        Outer(Stateful.modify<StateT<PgState, ReaderT<PgEnv, IO>>, PgState>(f));

    // ==================== Readable ====================

    public static K<Pg, A> Asks<A>(Func<PgEnv, A> f) =>
        Outer(new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
            Readable.asks<ReaderT<PgEnv, IO>, PgEnv, (A, PgState)>(env => (f(env), state))));

    public static K<Pg, A> Local<A>(Func<PgEnv, PgEnv> f, K<Pg, A> ma)
    {
        return new Pg<A>(
            new StateT<PgState, ReaderT<PgEnv, IO>, A>(state =>
                Readable.local(f, ma.As().runPg.Run(state))));
    }

    // ==================== Conversion ====================

    public static Pg<A> As<A>(K<Pg, A> ma) => (Pg<A>)ma;
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
    public static Pg<Unit> Ignore<A>(this Pg<A> ma) =>
        ma.Map(_ => unit);
}
