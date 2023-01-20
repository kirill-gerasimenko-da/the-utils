namespace TheUtils.NewTypePredicates;

using LanguageExt.TypeClasses;

public struct NotEmptyGuid : Pred<Guid>
{
    public bool True(Guid value) => value != Guid.Empty;
}