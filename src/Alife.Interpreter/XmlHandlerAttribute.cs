namespace Alife.Interpreter;

/// <summary>
/// 标记一个方法为 XML 标签处理程序。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class XmlHandlerAttribute : Attribute
{
    public string TagName { get; }

    public XmlHandlerAttribute(string tagName)
    {
        TagName = tagName;
    }
}
