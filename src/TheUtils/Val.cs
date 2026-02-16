// ReSharper disable UnusedMethodReturnValue.Global
// ReSharper disable MemberCanBePrivate.Global
namespace TheUtils;

using FluentValidation;
using FluentValidation.Results;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using static LanguageExt.Prelude;

public static class Val
{
    public delegate IRuleBuilderOptions<A, A> RuleBuilder<A>(IRuleBuilder<A, A> builder);

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

    // ===== Extension blocks =====

    extension<A>(AbstractValidator<A> val)
    {
        public IConditionBuilder WhenSome<B>(
            Func<A, Option<B>> predicate,
            Action<IRuleBuilderInitial<A, B>> builder
        ) =>
            val.When(
                x => predicate(x).IsSome,
                () => builder(val.RuleFor(y => predicate(y).ValueUnsafe()))
            );
    }

    extension<A>(AbstractValidator<A> builder) where A : Validated<A>
    {
        public IRuleBuilderOptions<A, A> SetFluentValidator(params string[] ruleSets) =>
            builder.RuleFor(x => x).SetFluentValidator(ruleSets);
    }

    extension<A, B>(IRuleBuilder<A, B> builder) where B : Validated<B>
    {
        public IRuleBuilderOptions<A, B> SetFluentValidator(params string[] ruleSets) =>
            builder.NotNull().SetValidator(fluentValidator<B>(), ruleSets);
    }

    extension(ValidationResult result)
    {
        public Error ToError(string message) => toError(result, message);
    }

    extension<A>(A a) where A : Validated<A>
    {
        public A Validate() => a.Validate(A.validator);

        public Option<A> ValidateSafe() => isValid(a) ? Some(a) : None;

        public bool IsValid() => validate(a, A.validator).IsValid;

        public ValidationResult TryValidate() => validate(a, A.validator);

        public Eff<A> ValidateEff() => validateEff(a);

        public Fin<A> ValidateFin(string error) => a.ValidateFin(() => error);

        public Fin<A> ValidateFin(Func<string> error)
        {
            var r = tryValidate(a);
            return r.IsValid ? a : r.ToError(error());
        }
    }

    extension<A>(A value)
    {
        public A Validate(Validator<A> validator)
        {
            var result = validate(value, validator);
            if (result.IsValid)
                return value;

            throw toError(result, $"Validation failed for object of type '{typeof(A).Name}'");
        }

        public Option<A> ValidateSafe(Validator<A> validator) =>
            validate(value, validator).IsValid ? value : None;

        public Eff<A> ValidateEff(Validator<A> validator) =>
            validateEff(value, validator);
    }

    // ===== Delegate and non-extension methods =====

    public delegate void Validator<A>(AbstractValidator<A> validator);

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

    public static ValidationResult validate<A>(A value, Validator<A> validator) =>
        new ValidatorImpl<A>(validator).Validate(value);

    public static Eff<A> validateEff<A>(
        A a,
        Func<IRuleBuilder<A, A>, IRuleBuilderOptions<A, A>> ruleBuilder
    ) => a.ValidateEff(x => ruleBuilder(x.RuleFor(v => v)));

    public static Eff<A> validateEff<A>(A value, Validator<A> validator) =>
        from val in liftEff(() => validate(value, validator))
        from _ in guard(val.IsValid, mapToError<A>()(val))
        select value;

    public static Eff<A> validateEff<A>(A value)
        where A : Validated<A> => validateEff(value, A.validator);

    public static Fin<A> validateFin<A>(A a, Func<string> error)
        where A : Validated<A> => a.ValidateFin(error);

    public static Fin<A> validateFin<A>(A a, string error)
        where A : Validated<A> => a.ValidateFin(error);

    // ===== Private helpers =====

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

    static Func<ValidationResult, Error> mapToError<A>() =>
        result => toError(result, $"Validation failed for object of type '{typeof(A).Name}'");

    class ValidatorImpl<A> : AbstractValidator<A>
    {
        public ValidatorImpl(Validator<A> validate) => validate(this);
    }

    static Validation<Error, string> notEmpty(string value, Error error) =>
        Optional(value).Bind(Ext.ifEmptyNone).ToValidation(error);

    public static Validation<Error, string> asNotEmpty(string value, Error error) =>
        notEmpty(value, error);

    public static Validation<Error, Uri> asUri(string value, Func<Error> error) =>
        Uri.IsWellFormedUriString(value, UriKind.Absolute) ? Pure(new Uri(value)) : Fail(error());

    public static Validation<Error, Uri> asUri(string value, Error error) =>
        Uri.IsWellFormedUriString(value, UriKind.Absolute) ? Pure(new Uri(value)) : Fail(error);

    public static Validation<Error, bool> asBool(string value, Func<Error> error) =>
        parseBool(value).ToValidation(error);

    public static Validation<Error, bool> asBool(string value, Error error) =>
        parseBool(value).ToValidation(error);

    public static Validation<Error, int> asInt(string value, Func<Error> error) =>
        parseInt(value).ToValidation(error);

    public static Validation<Error, int> asInt(string value, Error error) =>
        parseInt(value).ToValidation(error);

    public static Validation<Error, long> asLong(string value, Func<Error> error) =>
        parseLong(value).ToValidation(error);

    public static Validation<Error, long> asLong(string value, Error error) =>
        parseLong(value).ToValidation(error);

    public static Validation<Error, decimal> asDecimal(string value, Func<Error> error) =>
        parseDecimal(value).ToValidation(error);

    public static Validation<Error, decimal> asDecimal(string value, Error error) =>
        parseDecimal(value).ToValidation(error);

    public static Validation<Error, double> asDouble(string value, Func<Error> error) =>
        parseDouble(value).ToValidation(error);

    public static Validation<Error, double> asDouble(string value, Error error) =>
        parseDouble(value).ToValidation(error);

    public static Validation<Error, T> asEnum<T>(string value, Func<Error> error)
        where T : struct => parseEnumIgnoreCase<T>(value).ToValidation(error);

    public static Validation<Error, T> asEnum<T>(string value, Error error)
        where T : struct => parseEnumIgnoreCase<T>(value).ToValidation(error);

    public static Validation<Error, TimeSpan> asTimeSpan(string value, Func<Error> error) =>
        parseTimeSpan(value).ToValidation(error);

    public static Validation<Error, TimeSpan> asTimeSpan(string value, Error error) =>
        parseTimeSpan(value).ToValidation(error);

    public static Validation<Error, Guid> asGuid(string value, Func<Error> error) =>
        parseGuid(value).ToValidation(error);

    public static Validation<Error, Guid> asGuid(string value, Error error) =>
        parseGuid(value).ToValidation(error);

    public static Validation<Error, DateTimeOffset> asDateTimeOffset(
        string value,
        Func<Error> error
    ) => parseDateTimeOffset(value).ToValidation(error);

    public static Validation<Error, DateTimeOffset> asDateTimeOffset(string value, Error error) =>
        parseDateTimeOffset(value).ToValidation(error);
}
