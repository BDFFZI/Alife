namespace Alife.Interpreter;

[AttributeUsage(AttributeTargets.Method)]
public class XmlHandlerAttribute : Attribute
{
    public string? TagName { get; }
    public string Description { get; }

    public XmlHandlerAttribute(string? tagName = null, string description = "")
    {
        TagName = tagName;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Parameter)]
public class XmlTagContentAttribute : Attribute { }
