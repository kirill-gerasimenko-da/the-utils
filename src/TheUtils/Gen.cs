namespace TheUtils;

/// <summary>
/// Apply to a partial class to generate interface from its public instance methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class GenerateInterfaceAttribute : Attribute;
