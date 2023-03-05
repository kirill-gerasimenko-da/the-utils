// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable VirtualMemberCallInConstructor

// ReSharper disable TypeParameterCanBeVariant

namespace TheUtils;

using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static partial class Functions
{
    public interface IFunctionAff<TInput, TOutput>
    {
        Aff<TOutput> Invoke(TInput input, CancellationToken token);
    }

    public interface IFunctionEff<TInput, TOutput>
    {
        Eff<TOutput> Invoke(TInput input);
    }

    public interface IConvertibleFunction<TAff, TAsync, TUnsafe>
    {
        TAff ToAff();
        TAsync ToSafe();
        TUnsafe ToUnsafe();
    }

    public delegate void InputValidator<TInput>(AbstractValidator<TInput> validator);

    class ValidatorImpl<T> : AbstractValidator<T>
    {
        public ValidatorImpl(InputValidator<T> registerValidators) => registerValidators(this);
    }

    static Error validationError(string message) => Error.New(new ValidationException(message));

    static Error validationError(IEnumerable<ValidationFailure> failures) =>
        Error.New(new ValidationException(failures));

    public abstract class FunctionAff<TInput, TOutput> : IFunctionAff<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected FunctionAff() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public Aff<TOutput> Invoke(TInput input, CancellationToken token) =>
            from _1 in guardnot(isnull(input), validationError("Input could not be null"))
            from validationResult in Eff(() => _validator.Validate(input))
            from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
            from output in DoInvoke(input, token)
            select output;

        protected abstract Aff<TOutput> DoInvoke(TInput input, CancellationToken token);
    }

    public abstract class FunctionAff<TOutput> : IFunctionAff<Unit, TOutput>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Aff<TOutput> Invoke(Unit _, CancellationToken token) => DoInvoke(token);

        protected abstract Aff<TOutput> DoInvoke(CancellationToken token);
    }

    public abstract class FunctionAff : IFunctionAff<Unit, Unit>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Aff<Unit> Invoke(Unit _, CancellationToken token) => DoInvoke(token);

        protected abstract Aff<Unit> DoInvoke(CancellationToken token);
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
            from output in DoInvoke(input)
            select output;

        protected abstract Eff<TOutput> DoInvoke(TInput input);
    }
    
    public abstract class FunctionEff<TOutput> : IFunctionEff<Unit, TOutput>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Eff<TOutput> Invoke(Unit _) => DoInvoke();

        protected abstract Eff<TOutput> DoInvoke();
    }

    public abstract class FunctionEff : IFunctionEff<Unit, Unit>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Eff<Unit> Invoke(Unit _) => DoInvoke();

        protected abstract Eff<Unit> DoInvoke();
    }
    
}