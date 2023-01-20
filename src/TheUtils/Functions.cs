// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable VirtualMemberCallInConstructor

namespace TheUtils;

using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static class Functions
{
    public interface IFunctionAff<in TInput, TOutput>
    {
        Aff<TOutput> Invoke(TInput input, CancellationToken token);
    }

    public interface IFunctionAsync<in TInput, TOutput>
    {
        ValueTask<Fin<TOutput>> Invoke(TInput input, CancellationToken token);
    }

    public interface IFunctionEff<in TInput, TOutput>
    {
        Eff<TOutput> Invoke(TInput input);
    }

    public interface IFunction<in TInput, TOutput>
    {
        Fin<TOutput> Invoke(TInput input);
    }

    public delegate void InputValidator<TInput>(AbstractValidator<TInput> validator);

    class ValidatorImpl<T> : AbstractValidator<T>
    {
        public ValidatorImpl(InputValidator<T> registerValidators) => registerValidators(this);
    }

    static Error validationError(string message) => Error.New(new ValidationException(message));

    static Error validationError(IEnumerable<ValidationFailure> failures) =>
        Error.New(new ValidationException(failures));

    public abstract class FunctionAsync<TInput, TOutput> : IFunctionAsync<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected FunctionAsync() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public ValueTask<Fin<TOutput>> Invoke(TInput input, CancellationToken token)
        {
            var expr =
                from _1 in guardnot(isnull(input), validationError("Input could not be null"))
                from validationResult in Eff(() => _validator.Validate(input))
                from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
                from output in AffMaybe(async () => await invoke(input, token))
                select output;

            return expr.Run();
        }

        protected abstract ValueTask<Fin<TOutput>> invoke(TInput input, CancellationToken token);
    }

    public abstract class Function<TInput, TOutput> : IFunction<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected Function() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public Fin<TOutput> Invoke(TInput input)
        {
            var expr =
                from _1 in guardnot(isnull(input), validationError("Input could not be null"))
                from validationResult in Eff(() => _validator.Validate(input))
                from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
                from output in EffMaybe(() => invoke(input))
                select output;

            return expr.Run();
        }

        protected abstract Fin<TOutput> invoke(TInput input);
    }

    public abstract class FunctionAff<TInput, TOutput> : IFunctionAff<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected FunctionAff() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public Aff<TOutput> Invoke(TInput input, CancellationToken token) =>
            from _1 in guardnot(isnull(input), validationError("Input could not be null"))
            from validationResult in Eff(() => _validator.Validate(input))
            from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
            from output in invoke(input, token)
            select output;

        protected abstract Aff<TOutput> invoke(TInput input, CancellationToken token);
    }

    public abstract class FunctionEff<TInput, TOutput> : IFunctionEff<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected FunctionEff() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public Eff<TOutput> Invoke(TInput input) =>
            from _1 in guardnot(isnull(input), validationError("Input could not be null"))
            from validationResult in Eff(() => _validator.Validate(input))
            from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
            from output in invoke(input)
            select output;

        protected abstract Eff<TOutput> invoke(TInput input);
    }
}