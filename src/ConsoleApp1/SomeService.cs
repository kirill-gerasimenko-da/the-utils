namespace ConsoleApp1;

using System.Threading.Tasks;
using DataQuery.LanguageExt.Sql;
using LanguageExt;
using static LanguageExt.Prelude;
using static Database;

public class SomeService
{
    readonly IGetLatestUser _getLatestUserObject;
    readonly GetLatestUserAff _getLatestUser;
    readonly GetLatestUserSafe _getLatestUserSafe;
    readonly GetLatestUserUnsafe _getLatestUserUnsafe;
    readonly FindUserNameByIdEffect _query;

    public SomeService(
        IGetLatestUser getLatestUserObject,
        StartJobEff start,
        DeleteUserAff deluser,
        GetLatestUserAff getLatestUser,
        GetLatestUserSafe getLatestUserAsync,
        GetLatestUserUnsafe getLatestUserUnsafe,
        FindUserNameByIdAff query)
    {
        _getLatestUserObject = getLatestUserObject;
        _getLatestUser = getLatestUser;
        _getLatestUserSafe = getLatestUserAsync;
        _getLatestUserUnsafe = getLatestUserUnsafe;
        _query = query;
    }

    public async Task<int> SomeMethod(CancellationToken token)
    {
        var rs =
            from r in _getLatestUser("user name", 400, token)
            from r1 in _query(10, "kir")
            select (r.UserCount, r1.IfNone("hello-kir"));
        
        DataQuerySql.ISqlDatabase db = null;
        
        var rrr = await db.RunOrFail(rs, token);
        
        var resultAff = _getLatestUser("user name", 400, token);
        var resultFin = await _getLatestUserSafe("test", 412, token);
        var resultValue = await _getLatestUserUnsafe("user name", 400, token);
        //
        // aff direct call 
        var result = (await _getLatestUserObject.Invoke(new("test", 699), token).Run()).ThrowIfFail();

        return 42;
    }
}