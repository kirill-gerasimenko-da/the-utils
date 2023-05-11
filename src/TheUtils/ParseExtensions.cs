// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using System.Text.Json;
using LanguageExt;
using static LanguageExt.Prelude;

public static class ParseExtensions
{
    public static Option<Uri> ParseUri(this string uri) => parseUri(uri);

    public static Option<T> ParseJson<T>(this string json, Option<JsonSerializerOptions> options = default) =>
        parseJson<T>(json, options);

    public static Option<Uri> parseUri(string uri, UriKind kind = UriKind.Absolute)
    {
        if (isEmpty(uri))
            return None;

        if (Uri.TryCreate(uri, kind, out var result))
            return result;

        return None;
    }

    public static Option<T> parseJson<T>(string json, Option<JsonSerializerOptions> options = default)
    {
        if (isEmpty(json))
            return None;

        try
        {
            return JsonSerializer.Deserialize<T>(json, options.IfNoneDefault());
        }
        catch
        {
            return None;
        }
    }
}