namespace TheUtils.UnitTests;

using FluentAssertions;
using FluentAssertions.Equivalency;

public static class FluentAssertionExtensions
{
    public static bool IsEquivalentTo<T>(this T object1, T object2)
    {
        try
        {
            object1.Should().BeEquivalentTo(object2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsEquivalentTo<TExp>(this TExp object1, TExp object2,
        Func<EquivalencyAssertionOptions<TExp>, EquivalencyAssertionOptions<TExp>> config)
    {
        try
        {
            object1.Should().BeEquivalentTo(object2, config: config);
            return true;
        }
        catch
        {
            return false;
        }
    }
}