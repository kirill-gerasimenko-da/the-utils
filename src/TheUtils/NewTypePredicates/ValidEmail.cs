namespace TheUtils.NewTypePredicates;

using System.Text.RegularExpressions;
using LanguageExt.TypeClasses;

public struct ValidEmail : Pred<string>
{
    private static readonly Regex EmailRe =
        new(@"^(([^<>()\[\]\.,;:\s@""]+(\.[^<>()\[\]\.,;:\s@""]+)*)|("".+""))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$");

    public bool True(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 100 &&
        EmailRe.Match(value).Success;
}