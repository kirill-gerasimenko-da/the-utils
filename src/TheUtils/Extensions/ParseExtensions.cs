// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils.Extensions;

using LanguageExt;
using Newtonsoft.Json;
using static LanguageExt.Prelude;

public static partial class TheUtilsExtensions
{
    public static Option<Uri> ParseUri(this string uri) => parseUri(uri);

    public static Option<T> ParseJson<T>(
        this string json,
        Option<JsonSerializerSettings> settings = default
    ) => parseJson<T>(json, settings);

    public static Option<Uri> parseUri(string uri, UriKind kind = UriKind.Absolute)
    {
        if (isEmpty(uri))
            return None;

        if (Uri.TryCreate(uri, kind, out var result))
            return result;

        return None;
    }

    public static Option<T> parseJson<T>(
        string json,
        Option<JsonSerializerSettings> settings = default
    )
    {
        if (isEmpty(json))
            return None;

        try
        {
            return JsonConvert.DeserializeObject<T>(json, settings.IfNoneDefault());
        }
        catch
        {
            return None;
        }
    }
}