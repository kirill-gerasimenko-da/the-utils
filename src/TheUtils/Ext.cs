// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static class Ext
{
    extension<T>(Option<T> opt) where T : class
    {
        public T IfNoneDefault() => opt.IsNone ? default : opt.ValueUnsafe();
    }

    extension(string s)
    {
        public Option<string> IfEmptyNone() => isEmpty(s) ? None : Some(s);
    }

    extension<T>(Option<T> o)
    {
        public bool IsSome(out T value)
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

    // ignore operators

    extension<A>(IO<A> _)
    {
        public static IO<Unit> operator ~(IO<A> ma) => ma.Map(_ => unit);
    }

    extension<A>(Eff<A> _)
    {
        public static Eff<Unit> operator ~(Eff<A> ma) => ma.Map(_ => unit);
    }

    extension<RT, A>(Eff<RT, A> _)
    {
        public static Eff<RT, Unit> operator ~(Eff<RT, A> ma) => ma.Map(_ => unit);
    }

    public static T ifNoneDefault<T>(Option<T> opt)
        where T : class => opt.IfNoneDefault();

    public static Option<string> ifEmptyNone(string s) => s.IfEmptyNone();

    public static bool isSome<T>(Option<T> o, out T value) => o.IsSome(out value);
}
