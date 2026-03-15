namespace Alife.Interpreter;

/// <summary>
/// 标签信息，包含标签名和属性。
/// </summary>
public readonly struct TagInfo
{
    public string Name { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// 解析器上下文信息，可选地注入到标签处理程序中。
/// </summary>
public class XmlTagContext
{
    public IReadOnlyList<TagInfo> CallChain { get; }

    /// <summary>触发本次处理的字符串（如果是因标点断句触发则不为 null）</summary>
    public string? Trigger { get; }

    public XmlTagContext(IReadOnlyList<TagInfo> stack, string? trigger)
    {
        CallChain = stack;
        Trigger = trigger;
    }

    /// <summary>
    /// 检查调用链中是否包含指定的标签（类似 CSS 祖先选择器）。
    /// </summary>
    public bool HasAncestor(string tagName)
        => CallChain.Any((TagInfo t) => t.Name == tagName);

    /// <summary>
    /// 获取调用链的路径字符串表示，例如 "chat > video"。
    /// </summary>
    public string GetPath()
        => string.Join(" > ", CallChain.Select((TagInfo t) => t.Name));
}
