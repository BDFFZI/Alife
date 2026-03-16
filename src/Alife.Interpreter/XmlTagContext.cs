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

    /// <summary>当前正在处理的标签是否已经完全闭合（如果是，则表示这是该标签最后一次触发）</summary>
    public bool IsClosing { get; }

    public XmlTagContext(IReadOnlyList<TagInfo> stack, string? trigger, bool isClosing = false)
    {
        CallChain = stack;
        Trigger = trigger;
        IsClosing = isClosing;
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
