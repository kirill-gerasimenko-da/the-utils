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

    public interface IConvertibleFunction<TEffect, TAsync, TUnsafe>
    {
        TEffect ToEffect();
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

        protected CancellationToken _token { get; private set; }

        protected FunctionAff() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        public Aff<TOutput> Invoke(TInput input, CancellationToken token)
        {
            _token = token;

            return from _1 in guardnot(isnull(input), validationError("Input could not be null"))
                from validationResult in Eff(() => _validator.Validate(input))
                from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
                from output in InvokeAff(input)
                select output;
        }

        protected abstract Aff<TOutput> InvokeAff(TInput input);
    }

    public abstract class FunctionAff<TOutput> : IFunctionAff<Unit, TOutput>
    {
        protected CancellationToken _token { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Aff<TOutput> Invoke(Unit _, CancellationToken token)
        {
            _token = token;
            return InvokeAff();
        }

        protected abstract Aff<TOutput> InvokeAff();
    }

    public abstract class FunctionAff : IFunctionAff<Unit, Unit>
    {
        protected CancellationToken _token { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Aff<Unit> Invoke(Unit _, CancellationToken token)
        {
            _token = token;
            return InvokeAff();
        }

        protected abstract Aff<Unit> InvokeAff();
    }

    public abstract class FunctionAsync<TInput, TOutput> : FunctionAff<TInput, TOutput>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override Aff<TOutput> InvokeAff(TInput input) =>
            AffMaybe(async () => await InvokeAsync(input));

        protected abstract Task<Fin<TOutput>> InvokeAsync(TInput input);
    }

    public abstract class FunctionAsync<TOutput> : FunctionAff<Unit, TOutput>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override Aff<TOutput> InvokeAff(Unit _) =>
            AffMaybe(async () => await InvokeAsync());

        protected abstract Task<Fin<TOutput>> InvokeAsync();
    }

    public abstract class FunctionAsync : FunctionAff<Unit, Unit>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override Aff<Unit> InvokeAff(Unit _) =>
            AffMaybe(async () => await InvokeAsync());

        protected abstract Task<Fin<Unit>> InvokeAsync();
    }

    public abstract class FunctionEff<TInput, TOutput> : IFunctionEff<TInput, TOutput>
    {
        readonly ValidatorImpl<TInput> _validator;

        protected FunctionEff() => _validator = new ValidatorImpl<TInput>(Validator);

        protected virtual InputValidator<TInput> Validator { get; } = _ => { };

        Eff<TOutput> IFunctionEff<TInput, TOutput>.Invoke(TInput input) =>
            from _1 in guardnot(isnull(input), validationError("Input could not be null"))
            from validationResult in Eff(() => _validator.Validate(input))
            from _2 in guard(validationResult.IsValid, validationError(validationResult.Errors))
            from output in InvokeEff(input)
            select output;

        protected abstract Eff<TOutput> InvokeEff(TInput input);
    }

    public abstract class FunctionEff<TOutput> : IFunctionEff<Unit, TOutput>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Eff<TOutput> IFunctionEff<Unit, TOutput>.Invoke(Unit _) => InvokeEff();

        protected abstract Eff<TOutput> InvokeEff();
    }

    public abstract class FunctionEff : IFunctionEff<Unit, Unit>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Eff<Unit> IFunctionEff<Unit, Unit>.Invoke(Unit _) => InvokeEff();

        protected abstract Eff<Unit> InvokeEff();
    }
}