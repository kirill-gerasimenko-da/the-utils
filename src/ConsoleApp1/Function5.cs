namespace ConsoleApp1;

using System.Collections;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Effects.Traits;
using Microsoft.Extensions.DependencyInjection;
using TheUtils;
using static LanguageExt.Prelude;

public class GetDeviceUserId
{
    public GetDeviceUserId(IServiceProvider provider, Seq<int> ints)
    {
    }

    public Eff<string> Invoke(string manufacturer, CancellationToken token)
    {
        return SuccessEff("username");
    }
    
    public string Invoke2 (string manufacturer, CancellationToken token)
    {
        return "username";
    }
}

// generated

public delegate Eff<string> GetDeviceUserIdEff(string manufacturer, CancellationToken token);
public delegate Fin<string> GetDeviceUserIdSafe(string manufacturer, CancellationToken token);
public delegate string GetDeviceUserIdUnsafe(string manufacturer, CancellationToken token);

public static partial class ServiceCollectionFunctionExtensions
{
    public static IServiceCollection AddGetDeviceUserIdFunction
    (
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
    {
        services.Add(new(
            serviceType: typeof(GetDeviceUserId),
            implementationType: typeof(GetDeviceUserId),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceUserIdEff),
            factory: x => new GetDeviceUserIdEff(
                (manufacturer, token) =>
                    from result in x.GetRequiredService<GetDeviceUserId>().Invoke(manufacturer, token)
                    select result),
            lifetime));
        
        services.Add(new(
            serviceType: typeof(GetDeviceIdAff),
            factory: x => new GetDeviceIdAff(
                (manufacturer, token) =>
                    from result in x.GetRequiredService<GetDeviceId>().Invoke3(manufacturer, token).ToAff().Bind(v => v.ToAff())
                    select result),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceIdSafe),
            factory: x => new GetDeviceIdSafe(
                async (manufacturer, token) =>
                    await x.GetRequiredService<GetDeviceIdAff>()(manufacturer, token).Run()),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceIdUnsafe),
            factory: x => new GetDeviceIdUnsafe(
                async (manufacturer, token) =>
                    await x.GetRequiredService<GetDeviceIdAff>()(manufacturer, token).RunUnsafe()),
            lifetime));

        return services;
    }
}