namespace TheUtils;

using System.Runtime.CompilerServices;
using LanguageExt;
using static LanguageExt.Prelude;

public static class FunctionTransforms
{
    // affects
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aff<T> Transform<T>(Func<Aff<T>> aff) =>
        Eff(aff).Bind(identity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aff<T> Transform<T>(Func<Aff<Fin<T>>> aff) =>
        Eff(aff).Bind(identity).Bind(static x => x.ToAff());

    // effects
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Eff<T> Transform<T>(Func<Eff<T>> eff) =>
        Eff(eff).Bind(identity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Eff<T> Transform<T>(Func<Eff<Fin<T>>> eff) =>
        Eff(eff).Bind(identity).Bind(static x => x.ToEff());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Eff<Unit> Transform(Action action) =>
        Eff(() => { action(); return unit; });
    
    // fins
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Eff<T> Transform<T>(Func<Fin<T>> fin) =>
        Eff(fin).Bind(static x => x.ToEff());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Eff<T> Transform<T>(Func<Fin<Fin<T>>> fin) =>
        Eff(fin).Bind(static x => x.ToEff()).Bind(static x => x.ToEff());

    // tasks
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aff<Unit> Transform(Func<ValueTask> func) =>
        Aff(async () => await func().ToUnit());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aff<T> Transform<T>(Func<ValueTask<T>> func) =>
        Aff(async () => await func());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aff<T> Transform<T>(Func<ValueTask<Fin<T>>> func) =>
        Aff(async () => await func()).Bind(static x => x.ToAff());
}