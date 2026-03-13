namespace Alife.Core;

internal delegate Task CompiledTagInvoker(
    XmlTagContext context,
    ref string content,
    IReadOnlyDictionary<string, string> attributes);

/// <summary>
/// 编译后的标签处理程序映射表。
/// </summary>
public class XmlHandlerTable
{
    internal IReadOnlyDictionary<string, CompiledTagInvoker> Handlers { get; }

    public IEnumerable<string> RegisteredTags => Handlers.Keys;

    internal XmlHandlerTable(Dictionary<string, CompiledTagInvoker> handlers)
    {
        Handlers = handlers;
    }

    /// <summary>
    /// 检查是否包含指定标签的处理程序。
    /// </summary>
    public bool HasHandler(string tagName) => Handlers.ContainsKey(tagName);
}
