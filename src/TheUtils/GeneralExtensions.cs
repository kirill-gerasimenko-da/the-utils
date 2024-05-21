// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static class GeneralExtensions
{
    // option
    public static T IfNoneDefault<T>(this Option<T> opt)
        where T : class => opt.Match(s => s, () => default);

    public static Option<T> ToOption<T>(this T o)
        where T : class => Optional(o);

    public static Option<string> NoneIfEmpty(this string s) => isEmpty(s) ? None : Some(s);

    public static Option<string> NoneIfEmpty(this Option<string> s) => s.Bind(NoneIfEmpty);

    public static bool IsSome<T>(this Option<T> o, out T value)
    {
        if (o.IsNone)
        {
            value = default;
            return false;
        }

        value = o.ValueUnsafe();
        return true;
    }

    public static async Task<Option<T>> ToOption<T>(this Task<T> t)
        where T : class
    {
        var r = await t;
        return Optional(r);
    }

    // ignore
    public static async ValueTask Ignore(this ValueTask<Unit> unitTask) => await unitTask;

    public static async Task Ignore(this Task<Unit> unitTask) => await unitTask;

    public static void Ignore(this Unit _) { }

    // errors
    public static async ValueTask<T> ThrowIfFail<T>(this ValueTask<Fin<T>> task) =>
        (await task).ThrowIfFail();
}