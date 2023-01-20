// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using static LanguageExt.Prelude;

public static class StringExtensions
{
    public static Option<string> Substring(this string str, int start, int length) =>
        substring(str, start, length);

    public static Option<string> substring(string str, int start, int length)
    {
        if (string.IsNullOrEmpty(str) || start < 0 || length < 0)
            return None;

        if (start > str.Length)
            return None;

        return str.Substring(start, Math.Min(length, str.Length - start));
    }
}