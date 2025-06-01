// ReSharper disable UnusedMethodReturnValue.Global
// ReSharper disable MemberCanBePrivate.Global
namespace TheUtils;

using System.Runtime.CompilerServices;
using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static class Val
{
    public static readonly Atom<int> ValidationErrorCode = Atom(-10_000);

    public interface Validated<SELF>
        where SELF : Validated<SELF>
    {
        static abstract Validator<SELF> validator { get; }
    }

    public record Valid<A>
        where A : Validated<A>
    {
        public Valid(A a) => Value = validate(a);

        public A Value { get; }

        public static implicit operator Valid<A>(A a) => new(a);

        public static implicit operator A(Valid<A> a) => a.Value;
    }

    public static IConditionBuilder WhenSome<A, B>(
        this AbstractValidator<A> val,
        Func<A, Option<B>> predicate,
        Action<IRuleBuilderInitial<A, B>> builder
    ) =>
        val.When(
            x => predicate(x).IsSome,
            () => builder(val.RuleFor(y => predicate(y).ValueUnsafe()))
        );

    public static IRuleBuilderOptions<A, B> SetFluentValidator<A, B>(
        this IRuleBuilder<A, B> builder,
        params string[] ruleSets
    )
        where B : Validated<B> => builder.NotNull().SetValidator(fluentValidator<B>(), ruleSets);

    public static AbstractValidator<A> fluentValidator<A>()
        where A : Validated<A> => new ValidatorImpl<A>(A.validator);

    public static A validate<A>(A a)
        where A : Validated<A> => a.Validate();

    public static A validate<A>(
        A a,
        Func<IRuleBuilder<A, A>, IRuleBuilderOptions<A, A>> ruleBuilder
    ) => a.Validate(x => ruleBuilder(x.RuleFor(v => v)));

    public static Option<A> validateSafe<A>(
        A a,
        Func<IRuleBuilder<A, A>, IRuleBuilderOptions<A, A>> ruleBuilder
    ) => a.ValidateSafe(x => ruleBuilder(x.RuleFor(v => v)));

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

    public static Option<A> ValidateSafe<A>(this A value, Validator<A> validator) =>
        validate(value, validator).IsValid ? value : None;

    public static Eff<Unit> ValidateEff<A>(
        this A value,
        Validator<A> validator,
        [CallerArgumentExpression("value")] string callerName = null
    ) => validateEff(value, validator, callerName);

    public static Eff<Unit> validateEff<A>(
        A value,
        Validator<A> validator,
        [CallerArgumentExpression("value")] string callerName = null
    ) =>
        from val in liftEff(() => validate(value, validator))
        from _ in guard(val.IsValid, mapToError<A>(callerName)(val))
        select unit;

    public static Error ToError(this ValidationResult result, string message) =>
        toError(result, message);

    public static Fin<A> ValidateFin<A>(this A a, string error)
        where A : Validated<A> => a.ValidateFin(() => error);

    public static Fin<A> ValidateFin<A>(this A a, Func<string> error)
        where A : Validated<A>
    {
        var r = tryValidate(a);
        return r.IsValid ? new Fin.Fail<A>(r.ToError(error())) : new Fin.Succ<A>(a);
    }

    public static Fin<A> validateFin<A>(A a, Func<string> error)
        where A : Validated<A> => a.ValidateFin(error);

    public static Fin<A> validateFin<A>(A a, string error)
        where A : Validated<A> => a.ValidateFin(error);

    static Error toError(ValidationResult result, string message) =>
        new Expected(
            message,
            ValidationErrorCode.Value,
            Error.Many(
                toSeq(result.Errors)
                    .Map<Error>(e => new Expected(
                        $"'{e.PropertyName}': {e.ErrorMessage}",
                        ValidationErrorCode.Value
                    ))
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
