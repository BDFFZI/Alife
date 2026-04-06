namespace Alife.Interpreter;

[AttributeUsage(AttributeTargets.Method)]
public class XmlFunctionAttribute : Attribute
{
    public string? Name { get; }

    public XmlFunctionAttribute(string? name = null)
    {
        Name = name;
    }
}
