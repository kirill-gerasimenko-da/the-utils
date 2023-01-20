namespace TheUtils.NewTypePredicates;

using LanguageExt.TypeClasses;

public struct NotEmptyString : Pred<string>
{
    public bool True(string value) => !string.IsNullOrEmpty(value);
}