namespace TheUtils;

/// <summary>
/// When applied to the class, the interface containing all it's public methods
/// is generated and the class is inherited from it.
/// <p>The class must be partial.</p>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class GenInterfaceAttribute : Attribute;

/// <summary>
/// <inheritdoc cref="GenInterfaceAttribute"/>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class GenerateInterfaceAttribute : Attribute;