// ReSharper disable UnusedMethodReturnValue.Global

namespace TheUtils;

using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

public static class Validations
{
    public interface Validated<SELF>
        where SELF : Validated<SELF>
    {
        static abstract Validator<SELF> validator { get; }
    }

    public static IRuleBuilderOptions<A, B> SetFluentValidator<A, B>(
        this IRuleBuilder<A, B> builder,
        params string[] ruleSets
    )
        where B : Validated<B> => builder.SetValidator(fluentValidator<B>(), ruleSets);

    public static AbstractValidator<A> fluentValidator<A>()
        where A : Validated<A> => new ValidatorImpl<A>(A.validator);

    public static A validate<A>(A a)
        where A : Validated<A> => a.Validate();

    public static Option<A> validateSafe<A>(A a)
        where A : Validated<A> => a.ValidateSafe();

    public static bool isValid<A>(A a)
        where A : Validated<A> => a.IsValid();

    public static bool isValid<A>(
        A a,
        Func<IRuleBuilder<A, A>, IRuleBuilderOptions<A, A>> ruleBuilder
    ) => validate(a, x => ruleBuilder(x.RuleFor(v => v))).IsValid;

    public static ValidationResult tryValidate<A>(A a)
        where A : Validated<A> => a.TryValidate();

    public static A Validate<A>(this A a)
        where A : Validated<A> => a.Validate(A.validator);

    public static Option<A> ValidateSafe<A>(this A a)
        where A : Validated<A> => isValid(a) ? Some(a) : None;

    public static bool IsValid<A>(this A a)
        where A : Validated<A> => validate(a, A.validator).IsValid;

    public static ValidationResult TryValidate<A>(this A a)
        where A : Validated<A> => validate(a, A.validator);

    public delegate void Validator<A>(AbstractValidator<A> validator);

    public static ValidationResult validate<A>(A value, Validator<A> validator) =>
        new ValidatorImpl<A>(validator).Validate(value);

    public static A Validate<A>(this A value, Validator<A> validator)
    {
        var result = validate(value, validator);
        if (result.IsValid)
            return value;

        throw toError(result, $"Validation failed for object of type '{typeof(A).Name}'");
    }

    public static Eff<Unit> validateM<A>(
        A value,
        Validator<A> validator,
        [CallerArgumentExpression("value")] string callerName = null
    ) =>
        from val in liftEff(() => validate(value, validator))
        from _ in guard(val.IsValid, mapToError<A>(callerName)(val))
        select unit;

    public static Error ToError(this ValidationResult result, string message) =>
        toError(result, message);

    static Error toError(ValidationResult result, string message) =>
        Error.New(
            message,
            Error.Many(
                toSeq(result.Errors).Map(e => Error.New($"'{e.PropertyName}': {e.ErrorMessage}"))
            )
        );

    static Func<ValidationResult, Error> mapToError<A>(string callerName) =>
        result =>
            toError(
                result,
                $"Validation failed for '{callerName}' for object of type '{typeof(A).Name}'"
            );

    public class ValidatorImpl<A> : AbstractValidator<A>
    {
        public ValidatorImpl(Validator<A> validate) => validate(this);
    }
}