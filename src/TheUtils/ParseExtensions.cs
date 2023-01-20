// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using static LanguageExt.Prelude;

public static class ParseExtensions
{
    public static Option<Uri> ParseUri(this string uri) => parseUri(uri);

    public static Option<Uri> parseUri(string uri)
    {
        if (isEmpty(uri))
            return None;

        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return result;

        return None;
    }
}