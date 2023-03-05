namespace ConsoleApp1;

using System.Threading.Tasks;
using LanguageExt;

public class SomeService
{
    readonly IGetLatestUser _getLatestUserObject;
    readonly GetLatestUserAff _getLatestUser;
    readonly GetLatestUserSafe _getLatestUserSafe;
    readonly GetLatestUserUnsafe _getLatestUserUnsafe;

    public SomeService(
        IGetLatestUser getLatestUserObject,
        GetLatestUserAff getLatestUser,
        GetLatestUserSafe getLatestUserAsync,
        GetLatestUserUnsafe getLatestUserUnsafe)
    {
        _getLatestUserObject = getLatestUserObject;
        _getLatestUser = getLatestUser;
        _getLatestUserSafe = getLatestUserAsync;
        _getLatestUserUnsafe = getLatestUserUnsafe;
    }
    
    public async Task<int> SomeMethod()
    {
        var resultAff = _getLatestUser("user name", 400, default);
        var resultFin = await _getLatestUserSafe("test", 412, default);
        var resultValue = await _getLatestUserUnsafe("user name", 400, default);
        //
        // aff direct call 
        var result = (await _getLatestUserObject.Invoke(new("test", 699), default).Run()).ThrowIfFail();
        
        return 42; 
    }
}
