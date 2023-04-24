namespace ConsoleApp1;

using System.Data;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;
using LanguageExt.Effects.Traits;
using Microsoft.Extensions.DependencyInjection;
using TheUtils;
using static Database;

public class FunctionAttribute : Attribute
{
}

public interface IFunction<RT, TOutput> where RT : struct, HasDependencies<RT>
{
    Aff<RT, TOutput> Invoke();
}

public abstract record FunctionRecordAff<RT, TInput, TOutput> : IFunction<RT, TOutput>
    where TInput : FunctionRecordAff<RT, TInput, TOutput>
    where RT : struct, HasDependencies<RT>
{
    protected virtual InlineValidator<TInput> Validator { get; } = new();
    protected abstract Aff<RT, TOutput> Invoke();

    static Error validationError(IEnumerable<ValidationFailure> failures) =>
        Error.New(new ValidationException(failures));

    Aff<RT, TOutput> IFunction<RT, TOutput>.Invoke() =>
        from validationResult in Eff(() => Validator.Validate(new ValidationContext<TInput>((dynamic)this)))
        from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
        from output in Invoke()
        select output;
}

public interface HasDependencies<out RT> : HasCancel<RT> where RT : struct, HasDependencies<RT>
{
    GetLatestUserAff GetLatestUser { get; }
}

[Function]
public partial record GetLatestUserName<RT>(int Id)
    where RT : struct, HasDependencies<RT>
{
    protected override InlineValidator<GetLatestUserName<RT>> Validator => new()
    {
        v => v.RuleFor(x => x.Id).NotEmpty(),
    };

    protected override Aff<RT, string> Invoke()
    {
        getLatestUser<RT>();

        throw new NotImplementedException();
    }
}

public partial record GetLatestUserName<RT> : FunctionRecordAff<RT, GetLatestUserName<RT>, string>
{
    // generated
    static HasDependencies<RT> rt<RT>() where RT : struct, HasDependencies<RT> => default(RT);
    static CancellationToken token<RT>() where RT : struct, HasDependencies<RT> => default(RT).CancellationToken;

    static GetLatestUserAff getLatestUser<RT>() where RT : struct, HasDependencies<RT> =>
        default(RT).GetLatestUser;

    public readonly struct Runtime : HasDependencies<Runtime>
    {
        Runtime
        (
            IServiceProvider serviceProvider,
            CancellationTokenSource cancelTokenSource,
            CancellationToken cancelToken
        )
        {
            ServiceProvider = serviceProvider;
            CancellationTokenSource = cancelTokenSource;
            CancellationToken = cancelToken;
        }

        public static Runtime New(IServiceProvider serviceProvider, CancellationToken cancelToken) =>
            new(serviceProvider, new CancellationTokenSource(), cancelToken);

        public Runtime LocalCancel
        {
            get
            {
                var tokenSource = new CancellationTokenSource();
                return new(ServiceProvider, tokenSource, tokenSource.Token);
            }
        }

        public IServiceProvider ServiceProvider { get; }
        public CancellationToken CancellationToken { get; }
        public CancellationTokenSource CancellationTokenSource { get; }

        // generated
        public GetLatestUserAff GetLatestUser => ServiceProvider.GetRequiredService<GetLatestUserAff>();
    }
}