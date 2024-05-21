// ReSharper disable MemberCanBePrivate.Global

namespace TheUtils;

using LanguageExt;
using static LanguageExt.Prelude;

public static class AttributeExtensions
{
    public static Option<TAttribute> TryGetAttribute<TAttribute>(this Type type)
        where TAttribute : Attribute => tryGetAttribute<TAttribute>(type);

    public static Option<TAttribute> TryGetAttribute<TEnum, TAttribute>(TEnum @enum)
        where TAttribute : Attribute
        where TEnum : Enum => tryGetAttribute<TEnum, TAttribute>(@enum);

    public static Option<TAttribute> tryGetAttribute<TAttribute>(Type type)
        where TAttribute : Attribute =>
        toSeq(type.GetCustomAttributes(typeof(TAttribute), false)).Cast<TAttribute>().Head;

    public static Option<TAttribute> tryGetAttribute<TEnum, TAttribute>(TEnum @enum)
        where TAttribute : Attribute
        where TEnum : Enum =>
        toSeq(
                @enum
                    .GetType()
                    .GetMember(@enum.ToString())[0]
                    .GetCustomAttributes(typeof(TAttribute), false)
            )
            .Cast<TAttribute>()
            .Head;
}