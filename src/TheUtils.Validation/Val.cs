namespace TheUtils.Validation;

using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static class Val
{
    public delegate void Validator<A>(AbstractValidator<A> validator);

    public static ValidationResult validate<A>(A value, Validator<A> validator) =>
        new ValidatorImpl<A>(validator).Validate(value);

    public static Eff<Unit> validateM<A>(
        A value,
        Validator<A> validator,
        [CallerArgumentExpression("value")] string callerName = null
    ) =>
        from val in liftEff(() => validate(value, validator))
        from _ in guard(val.IsValid, mapToError<A>(callerName)(val))
        select unit;

    static Func<ValidationResult, Error> mapToError<A>(string callerName) =>
        result =>
            Error.New(
                $"Validation failed for '{callerName}' of type '{typeof(A).Name}'",
                Error.Many(
                    toSeq(result.Errors)
                        .Map(e => Error.New($"'{e.PropertyName}': {e.ErrorMessage}"))
                )
            );

    class ValidatorImpl<A> : AbstractValidator<A>
    {
        public ValidatorImpl(Validator<A> validate) => validate(this);
    }
}