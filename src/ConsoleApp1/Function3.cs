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

public partial record GetUsernameById(int Id, string Caller)
{
    InlineValidator<GetUsernameById> Validator => new()
    {
        v => v.RuleFor(x => x.Id).GreaterThan(0)
    };

    Aff<string> Invoke(IServiceProvider provider)
    {
        return SuccessAff("username");
    }
}

// generated

public delegate Aff<string> GetUsernameByIdAff(int id, string caller, CancellationToken token);

public delegate ValueTask<Fin<string>> GetUsernameByIdSafe(int id, string caller, CancellationToken token);

public delegate ValueTask<string> GetUsernameByIdUnsafe(int id, string caller, CancellationToken token);

public partial record GetUsernameById
{
    static Error validationError(IEnumerable<ValidationFailure> failures) =>
        Error.New(new ValidationException(failures));

    CancellationToken Token { get; init; }

    public class Function
    {
        readonly IServiceProvider _provider;

        public Function(IServiceProvider provider)
        {
            _provider = provider;
        }

        public GetUsernameByIdAff ToAff() => (id, caller, token) =>
        {
            var instance = new GetUsernameById(id, caller)
            {
                Token = token
            };

            return from validationResult in Eff(() => instance.Validator.Validate(instance))
                from _ in guard(validationResult.IsValid, validationError(validationResult.Errors))
                from output in instance.Invoke(_provider)
                select output;
        };

        public GetUsernameByIdSafe ToSafe() => async (id, caller, token) =>
            await ToAff()(id, caller, token).Run();

        public GetUsernameByIdUnsafe ToUnsafe() => async (id, caller, token) =>
            await ToAff()(id, caller, token).RunUnsafe();
    }
}

public static partial class ServiceCollectionFunctionExtensions
{
    public static IServiceCollection AddGetUsernameByIdFunction
    (
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    )
    {
        services.Add(new(
            serviceType: typeof(GetUsernameById.Function),
            implementationType: typeof(GetUsernameById.Function),
            lifetime));

        services.Add(new(
            serviceType: typeof(GetUsernameByIdAff),
            factory: x => x.GetRequiredService<GetUsernameById.Function>().ToAff(),
            lifetime));
        
        services.Add(new(
            serviceType: typeof(GetUsernameByIdSafe),
            factory: x => x.GetRequiredService<GetUsernameById.Function>().ToSafe(),
            lifetime));
        
        services.Add(new(
            serviceType: typeof(GetUsernameByIdUnsafe),
            factory: x => x.GetRequiredService<GetUsernameById.Function>().ToUnsafe(),
            lifetime));

        return services;
    }
}