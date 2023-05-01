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

public class GetDeviceId
{
    public GetDeviceId(IServiceProvider provider, Seq<int> ints)
    {
    }

    public Aff<string> Invoke(string manufacturer, CancellationToken token)
    {
        return SuccessAff("username");
    }

    public ValueTask<string> Invoke2(string manufacturer, CancellationToken token)
    {
        return SuccessAff("username");
    }

    public ValueTask<Fin<string>> Invoke3(string manufacturer, CancellationToken token)
    {
        return SuccessAff("username");
    }
}

// generated

public delegate Aff<string> GetDeviceIdAff(string manufacturer, CancellationToken token);

public delegate ValueTask<Fin<string>> GetDeviceIdSafe(string manufacturer, CancellationToken token);

public delegate ValueTask<string> GetDeviceIdUnsafe(string manufacturer, CancellationToken token);

public static partial class ServiceCollectionFunctionExtensions
{
    public static IServiceCollection AddGetDeviceIdFunction
    (
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
    {
        services.Add(new(
            serviceType: typeof(GetDeviceId),
            implementationType: typeof(GetDeviceId),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceIdAff),
            factory: x => new GetDeviceIdAff(
                (manufacturer, token) =>
                    Eff(() => x.GetRequiredService<GetDeviceId>().Invoke(manufacturer, token)).Bind(identity)
            ),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetDeviceIdAff),
            factory: x => new GetDeviceIdAff(
                (manufacturer, token) =>
                    Eff(() => x.GetRequiredService<GetDeviceId>().Invoke3(manufacturer, token).ToAff().Bind(v => v.ToAff())).Bind(identity)
            ),
            lifetime));
        
        services.Add(new(
            serviceType: typeof(GetDeviceIdAff),
            factory: x => new GetDeviceIdAff(
                (manufacturer, token) =>
                    Eff(() => x.GetRequiredService<GetDeviceId>().Invoke2(manufacturer, token).ToAff()).Bind(identity)
            ),
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