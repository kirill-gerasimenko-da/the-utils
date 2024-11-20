// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils.Extensions;

using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static partial class TheUtilsExtensions
{
    public static T IfNoneDefault<T>(this Option<T> opt)
        where T : class => opt.IsNone ? default : opt.ValueUnsafe();

    public static Option<string> NoneIfEmpty(this string s) => isEmpty(s) ? None : Some(s);

    public static Option<string> NoneIfEmpty(this Option<string> s) =>
        s.Bind(x => isEmpty(x) ? None : Some(x));

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
}