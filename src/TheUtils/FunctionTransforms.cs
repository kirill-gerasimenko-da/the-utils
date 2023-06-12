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