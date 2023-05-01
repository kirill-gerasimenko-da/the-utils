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

    public string Invoke2(string manufacturer, CancellationToken token)
    {
        return "username";
    }

    public Fin<string> Invoke3(string manufacturer, CancellationToken token)
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
                    Eff(() => x.GetRequiredService<GetDeviceUserId>().Invoke(manufacturer, token)).Bind(identity)
            ),
            lifetime));
        
        services.Add(new(
            serviceType: typeof(GetDeviceUserIdEff),
            factory: x => new GetDeviceUserIdEff(
                (manufacturer, token) =>
                    Eff(() => x.GetRequiredService<GetDeviceUserId>().Invoke2(manufacturer, token))
            ),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceUserIdEff),
            factory: x => new GetDeviceUserIdEff(
                (manufacturer, token) =>
                    Eff(() => x.GetRequiredService<GetDeviceUserId>().Invoke3(manufacturer, token)).Bind(v => v.ToEff())
            ),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceUserIdSafe),
            factory: x => new GetDeviceUserIdSafe(
                (manufacturer, token) =>
                    x.GetRequiredService<GetDeviceUserIdEff>()(manufacturer, token).Run()),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceUserIdUnsafe),
            factory: x => new GetDeviceUserIdUnsafe(
                (manufacturer, token) =>
                    x.GetRequiredService<GetDeviceUserIdEff>()(manufacturer, token).RunUnsafe()),
            lifetime));

        return services;
    }
}