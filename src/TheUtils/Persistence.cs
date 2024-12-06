namespace TheUtils;

using System.Data;
using LanguageExt;
using LanguageExt.Traits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

public static class Persistence<RT>
    where RT : Has<Eff, DbContext>
{
    public static Eff<RT, DbContext> context => PersistenceM<Eff, RT>.context.As();

    public static Eff<RT, DatabaseFacade> facade => context.Map(c => c.Database);

    #region seq
    public static Eff<RT, Seq<A>> seq<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.seq(query).As();

    public static Eff<RT, Seq<A>> seq<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.seq<A>(sql).As();

    public static Eff<RT, Seq<A>> seq<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.seq<A>(sql, @params).As();
    #endregion

    #region any
    public static Eff<RT, bool> any<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.any(query).As();

    public static Eff<RT, bool> any<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.any<A>(sql).As();

    public static Eff<RT, bool> any<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.any<A>(sql, @params).As();
    #endregion

    #region count
    public static Eff<RT, int> count<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.count(query).As();

    public static Eff<RT, int> count<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.count<A>(sql).As();

    public static Eff<RT, int> count<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.count<A>(sql, @params).As();
    #endregion

    #region head
    public static Eff<RT, Option<A>> head<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.head(query).As();

    public static Eff<RT, Option<A>> head<A>(IQueryable<A?> query)
        where A : struct => PersistenceM<Eff, RT>.head(query).As();

    public static Eff<RT, Option<A>> head<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.head<A>(sql).As();

    public static Eff<RT, Option<A>> headNullable<A>(FormattableString sql)
        where A : struct => PersistenceM<Eff, RT>.headNullable<A>(sql).As();

    public static Eff<RT, Option<A>> head<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.head<A>(sql, @params).As();

    public static Eff<RT, Option<A>> headNullable<A>(string sql, Seq<object> @params = default)
        where A : struct => PersistenceM<Eff, RT>.headNullable<A>(sql, @params).As();
    #endregion

    #region headT
    public static OptionT<Eff, A> headT<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.headT(query);

    public static OptionT<Eff, A> headT<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.headT<A>(sql).As();

    public static OptionT<Eff, A> headT<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.headT<A>(sql, @params).As();
    #endregion

    #region single
    public static Eff<RT, A> single<A>(IQueryable<A> query) =>
        PersistenceM<Eff, RT>.single(query).As();

    public static Eff<RT, A> single<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.single<A>(sql).As();

    public static Eff<RT, A> single<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.single<A>(sql, @params).As();
    #endregion

    public static Eff<RT, DbSet<A>> set<A>()
        where A : class => PersistenceM<Eff, RT>.set<A>().As();

    public static Eff<RT, int> saveChanges => PersistenceM<Eff, RT>.saveChanges.As();

    public static Eff<RT, int> execute(FormattableString sql) =>
        PersistenceM<Eff, RT>.execute(sql).As();

    public static Eff<RT, int> execute(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.execute(sql, @params).As();

    public static Eff<RT, IDbContextTransaction> beginTransaction(
        IsolationLevel isolation = IsolationLevel.Unspecified
    ) => PersistenceM<Eff, RT>.beginTransaction(isolation).As();

    public static Eff<RT, Unit> commitTransaction => PersistenceM<Eff, RT>.commitTransaction.As();

    public static Eff<RT, Unit> rollbackTransaction =>
        PersistenceM<Eff, RT>.rollbackTransaction.As();

    public static Eff<RT, IQueryable<A>> query<A>(FormattableString sql) =>
        PersistenceM<Eff, RT>.query<A>(sql).As();

    public static Eff<RT, IQueryable<A>> query<A>(string sql, Seq<object> @params = default) =>
        PersistenceM<Eff, RT>.query<A>(sql, @params).As();

    public static Eff<RT, EntityEntry<A>> add<A>(A a)
        where A : class => PersistenceM<Eff, RT>.add(a).As();

    public static Eff<RT, Unit> addRange<A>(Seq<A> a)
        where A : class => PersistenceM<Eff, RT>.addRange(a).As();

    public static Eff<RT, EntityEntry<A>> update<A>(A a)
        where A : class => PersistenceM<Eff, RT>.update(a).As();

    public static Eff<RT, Unit> updateRange<A>(Seq<A> a)
        where A : class => PersistenceM<Eff, RT>.updateRange(a).As();

    public static Eff<RT, EntityEntry<A>> delete<A>(A a)
        where A : class => PersistenceM<Eff, RT>.delete(a).As();

    public static Eff<RT, Unit> deleteRange<A>(Seq<A> a)
        where A : class => PersistenceM<Eff, RT>.deleteRange(a).As();
}