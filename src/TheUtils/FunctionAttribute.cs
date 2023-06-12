namespace TheUtils;

[AttributeUsage(AttributeTargets.Class)]
public class FunctionAttribute : Attribute
{
    public FunctionAttribute(bool voidToUnit = true) =>
        VoidToUnit = voidToUnit;

    public bool VoidToUnit { get; set; }
}