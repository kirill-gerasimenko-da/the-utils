// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using System.Text.RegularExpressions;
using LanguageExt;
using Newtonsoft.Json;
using static LanguageExt.Prelude;

public static class ParseExtensions
{
    static readonly Regex EmailRe =
        new(
            @"^(([^<>()\[\]\.,;:\s@""]+(\.[^<>()\[\]\.,;:\s@""]+)*)|("".+""))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$");

    public static Option<Uri> ParseUri(this string uri) => parseUri(uri);

    public static Option<T> ParseJson<T>(this string json, Option<JsonSerializerSettings> settings = default) =>
        parseJson<T>(json, settings);

    public static Option<Uri> parseUri(string uri, UriKind kind = UriKind.Absolute)
    {
        if (isEmpty(uri))
            return None;

        if (Uri.TryCreate(uri, kind, out var result))
            return result;

        return None;
    }

    public static Option<T> parseJson<T>(string json, Option<JsonSerializerSettings> settings = default)
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

    public static Option<string> parseEmail(string json)
    {
        if (isEmpty(json))
            return None;

        var match = EmailRe.Match(json);
        if (match.Success)
            return match.Value;

        return None;
    }
}