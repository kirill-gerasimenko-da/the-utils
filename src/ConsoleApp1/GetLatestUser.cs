namespace ConsoleApp1;

using System.Data;
using LanguageExt;
using TheUtils;
using static DataQuery.LanguageExt.Sql.DataQuerySql;

public static class Database
{
    [Function]
    public record FindUserNameById(long Id, string Caller) : SqlQuery<Option<string>>
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
}