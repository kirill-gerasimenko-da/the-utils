namespace ConsoleApp1;

using System.Data;
using System.Runtime.CompilerServices;
using DataQuery.LanguageExt.Sql;
using FluentValidation;
using LanguageExt;
using TheUtils;
using static LanguageExt.Prelude;
using static TheUtils.Functions;
using static DataQuery.LanguageExt.Sql.DataQuerySql;
using static TheUtils.Functions;

public static partial class Database
{
    public class SqlQueryReturningAttribute<TResult> : Attribute
    {
    }

    [SqlQueryReturning<Option<string>>]
    public partial record FindUserNameById(long Id, string Caller)
    {
        public override Aff<DefaultRT, Option<string>> AsAff() =>
            TryQueryFirst<string>(@"SELECT 'username'", new { id = Id, caller = Caller });
    }

    public delegate Aff<DefaultRT, Option<string>> FindUserNameByIdEffect(long id, string caller);

    public delegate Aff<Option<string>> FindUserNameByIdAff(
        long id,
        string caller,
        CancellationToken token,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    public delegate ValueTask<Fin<Option<string>>> FindUserNameByIdSafe(
        long id,
        string caller,
        CancellationToken token,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    public delegate ValueTask<Option<string>> FindUserNameByIdUnsafe(
        long id,
        string caller,
        CancellationToken token,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    public interface IConvertibleSqlQuery<TEffect, TAff, TAsync, TUnsafe>
    {
        TEffect ToEffect();
        TAff ToEffect(ISqlDatabase database);
        TAsync ToSafe(ISqlDatabase database);
        TUnsafe ToUnsafe(ISqlDatabase database);
    }

    public partial record FindUserNameById :
        SqlQuery<Option<string>>,
        IConvertibleSqlQuery<
            FindUserNameByIdEffect,
            FindUserNameByIdAff,
            FindUserNameByIdSafe,
            FindUserNameByIdUnsafe>
    {
        public FindUserNameByIdEffect ToEffect() => (id, caller) => new FindUserNameById(id, caller).AsAff();

        public FindUserNameByIdAff ToEffect(ISqlDatabase database) => (id, caller, token, isolation) =>
            AffMaybe(async () => await database.Run(ToEffect()(id, caller), isolation, token));

        public FindUserNameByIdSafe ToSafe(ISqlDatabase database) => async (id, caller, token, isolation) =>
            await database.Run(new FindUserNameById(id, caller), isolation, token);

        public FindUserNameByIdUnsafe ToUnsafe(ISqlDatabase database) => async (id, caller, token, isolation) =>
            await database.RunOrFail(new FindUserNameById(id, caller), isolation, token);
    }

    [AsDelegate]
    public partial class GetLatestUser : FunctionAff<GetLatestUser.InputType, GetLatestUser.ResultType>
    {
        public readonly record struct InputType
        (
            string UserName,
            int Id
        );

        public readonly record struct ResultType
        (
            int UserCount
        );

        protected override InputValidator<InputType> Validator { get; } = v =>
        {
            v.RuleFor(x => x.Id).GreaterThan(0);
            v.RuleFor(x => x.UserName).Empty();
        };

        public GetLatestUser( /* dependencies */)
        {
            IRecordFunction<string> f = new GetLatestUserName(10);
            var rt = GetLatestUserName.Runtime.New();
            var r = await f.Invoke<GetLatestUserName.Runtime>().Run(rt);
            
        }

        protected override Aff<ResultType> InvokeAff(InputType input) =>
            // SuccessAff("hello");
            SuccessAff(new ResultType(_token.GetHashCode()));
    }
}

[AsDelegate]
public partial class DeleteUser : FunctionAff<int>
{
    protected override Aff<int> InvokeAff()
    {
        throw new NotImplementedException();
    }
}

[AsDelegate]
public partial class StartJob : FunctionEff
{
    protected override Eff<Unit> InvokeEff()
    {
        throw new NotImplementedException();
    }
}