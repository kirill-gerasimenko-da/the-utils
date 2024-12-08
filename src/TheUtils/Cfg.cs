// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Configuration;
using static LanguageExt.Prelude;

public static class Cfg
{
    public record ConfigurationError(string ParamName, string ReasonItIsInvalid)
        : Expected($"'{ParamName}' is not valid: {ReasonItIsInvalid}", 1);

    static Error empty(string paramName) =>
        new ConfigurationError(paramName, "Value is either null or empty");

    static Error notValidUri(string paramName) =>
        new ConfigurationError(paramName, "Invalid URI format");

    static Error notBoolean(string paramName) =>
        new ConfigurationError(paramName, "Invalid boolean");

    static Error notInt(string paramName) => new ConfigurationError(paramName, "Invalid integer");

    static Error notLong(string paramName) => new ConfigurationError(paramName, "Invalid long");

    static Error notDecimal(string paramName) =>
        new ConfigurationError(paramName, "Invalid decimal");

    static Error notDouble(string paramName) => new ConfigurationError(paramName, "Invalid double");

    static Error notEnum<T>(string paramName) =>
        new ConfigurationError(paramName, $"Invalid enum of type {typeof(T).Name}");

    static Error notTimeSpan(string paramName) =>
        new ConfigurationError(paramName, "Invalid timespan");

    static Error notDateTimeOffset(string paramName) =>
        new ConfigurationError(paramName, "Invalid date time offset");

    static Error notGuid(string paramName) => new ConfigurationError(paramName, "Invalid GUID");

    public static Eff<string> notEmpty(string value, Error error) =>
        Optional(value).Where(s => !string.IsNullOrWhiteSpace(s)).ToEff(error);

    public static Eff<string> read(string paramName, IConfiguration config) =>
        notEmpty(config[paramName], empty(paramName));

    public static Eff<Uri> readUri(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        from _ in guard(Uri.IsWellFormedUriString(value, UriKind.Absolute), notValidUri(paramName))
        select new Uri(value);

    public static Eff<bool> readBool(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseBool(value)
        from _ in guard(parsed.IsSome, notBoolean(paramName))
        select parsed.ValueUnsafe();

    public static Eff<int> readInt(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseInt(value)
        from _ in guard(parsed.IsSome, notInt(paramName))
        select parsed.ValueUnsafe();

    public static Eff<long> readLong(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseLong(value)
        from _ in guard(parsed.IsSome, notLong(paramName))
        select parsed.ValueUnsafe();

    public static Eff<decimal> readDecimal(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseDecimal(value)
        from _ in guard(parsed.IsSome, notDecimal(paramName))
        select parsed.ValueUnsafe();

    public static Eff<double> readDouble(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseDouble(value)
        from _ in guard(parsed.IsSome, notDouble(paramName))
        select parsed.ValueUnsafe();

    public static Eff<T> readEnum<T>(string paramName, IConfiguration config)
        where T : struct =>
        from value in read(paramName, config)
        let parsed = parseEnumIgnoreCase<T>(value)
        from _ in guard(parsed.IsSome, notEnum<T>(paramName))
        select parsed.ValueUnsafe();

    public static Eff<TimeSpan> readTimeSpan(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseTimeSpan(value)
        from _ in guard(parsed.IsSome, notTimeSpan(paramName))
        select parsed.ValueUnsafe();

    public static Eff<Guid> readGuid(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseGuid(value)
        from _ in guard(parsed.IsSome, notGuid(paramName))
        select parsed.ValueUnsafe();

    public static Eff<DateTimeOffset> readDateTimeOffset(string paramName, IConfiguration config) =>
        from value in read(paramName, config)
        let parsed = parseDateTimeOffset(value)
        from _ in guard(parsed.IsSome, notDateTimeOffset(paramName))
        select parsed.ValueUnsafe();
}