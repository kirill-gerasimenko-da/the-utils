// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static class GeneralExtensions
{
    // option
    public static T IfNoneDefault<T>(this Option<T> opt) where T : class => opt.IfNoneUnsafe(default(T));
    public static Option<T> ToOption<T>(this object o) where T : class => Optional(o as T);
    public static Option<string> NoneIfEmpty(this string s) => isEmpty(s) ? None : Some(s);
    public static Option<string> NoneIfEmpty(this Option<string> s) => s.Bind(x => isEmpty(x) ? None : Some(x));

    // ignore
    public static async ValueTask Ignore(this ValueTask<Unit> unitTask) => await unitTask;
    public static async Task Ignore(this Task<Unit> unitTask) => await unitTask;
    public static void Ignore(this Unit _) { }

    // transformers
    public static Seq<T> Choose<T>(this Seq<Option<T>> seq) => seq.Choose(identity);
    public static Seq<T> TraverseChoose<T>(this Option<Seq<T>> opt) => opt.Traverse(identity).Choose(identity);
    public static Seq<Option<A>> Traverse<A>(this Option<Seq<A>> opt) => opt.Traverse(identity);

    // effects
    public static async Task<T> RunUnsafe<T>(this Aff<T> aff) => await aff.Run().ThrowIfFail();
    public static T RunUnsafe<T>(this Eff<T> eff) => eff.Run().ThrowIfFail();

    // errors
    public static async ValueTask<T> ThrowIfFail<T>(this ValueTask<Fin<T>> task) => (await task).ThrowIfFail();

    public static Aff<T> ErrorIfNone<T>(this Aff<Option<T>> aff, Error error) =>
        aff.Bind(result => result.ToAff(error));

    public static Aff<T> ErrorIfNone<T>(this Task<Option<T>> task, Error error) =>
        task.ToAff().Bind(result => result.ToAff(error));
    
    public static Eff<Unit> EffUnit(Action action) => Eff(() => { action(); return unit; });
}