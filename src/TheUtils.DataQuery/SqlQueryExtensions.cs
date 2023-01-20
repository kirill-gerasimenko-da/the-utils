// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils.DataQuery;

using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using static global::DataQuery.LanguageExt.Sql.DataQuerySql;

public static class SqlQueryExtensions
{
    public static Aff<DefaultRT, Unit> ToUnit<T>(this Aff<DefaultRT, T> queryAff) =>
        queryAff.Map(_ => unit);

    // failing if option value is None
    public static Aff<DefaultRT, T> IfNoneThenFail<T>(this SqlQuery<Option<T>> query, Error fail) =>
        query.AsAff().IfNoneThenFail(fail);

    public static Aff<DefaultRT, T> IfNoneThenFail<T>(this Aff<DefaultRT, Option<T>> queryAff, Error fail) =>
        queryAff.Bind(o => o.ToEff(fail));

    public static Aff<DefaultRT, T> IfNoneThenFail<T>(this Option<T> option, Error error) =>
        option.ToAff(error);

    // failing if option value is Some
    public static Aff<DefaultRT, Unit> IfSomeThenFail<T>(this Aff<DefaultRT, Option<T>> queryAff, Error error) =>
        queryAff.Bind(o => o.IsSome ? throw error : unit.AsQueryAff());

    // map/bind helpers for query objects
    public static Aff<DefaultRT, R> Map<T, R>(this SqlQuery<T> query, Func<T, R> map) =>
        query.AsAff().Map(map);

    public static Aff<DefaultRT, R> Bind<T, R>(this SqlQuery<T> query, Func<T, Aff<DefaultRT, R>> bind) =>
        query.AsAff().Bind(bind);

    public static Aff<DefaultRT, R> Bind<T, R>(this Aff<DefaultRT, T> queryAff, Func<T, Aff<DefaultRT, R>> bind) =>
        queryAff.Bind(bind);

    public static Aff<DefaultRT, R> Map<T, R>(this Aff<DefaultRT, T> queryAff, Func<T, R> map) =>
        queryAff.Map(map);

    public static Aff<DefaultRT, T> AsQueryAff<T>(this T value) => Eff(() => value);

    // helpers of embedding queries
    public static Aff<DefaultRT, T> IfNoneThenQuery<T>(this Option<T> option, Aff<DefaultRT, T> queryAff) =>
        option.Case switch
        {
            T value => value.AsQueryAff(),
            _ => queryAff
        };

    public static Aff<DefaultRT, V> IfSomeThenQuery<T, V>(
        this Option<T> option,
        Func<T, Aff<DefaultRT, V>> query,
        V defaultValue)
        =>
            option.Case switch
            {
                T value => query(value),
                _ => defaultValue.AsQueryAff()
            };

    public static Aff<DefaultRT, Option<V>> IfSomeThenQuery<T, V>(
        this Option<T> option,
        Func<T, Aff<DefaultRT, Option<V>>> query)
        =>
            option.Case switch
            {
                T value => query(value),
                _ => Eff(() => Option<V>.None)
            };
}