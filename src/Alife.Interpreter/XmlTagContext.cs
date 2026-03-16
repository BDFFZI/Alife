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
/// 标签处理状态。
/// </summary>
public enum TagStatus
{
    /// <summary>开区间：标签刚刚打开，尚未处理任何内容。</summary>
    Opening,
    /// <summary>内容：由于分词或标签嵌套触发的中间内容处理。</summary>
    Content,
    /// <summary>闭区间：标签即将关闭，处理最后的内容片段。</summary>
    Closing,
    /// <summary>单次调用：针对自闭合或孤儿标签的特殊状态，仅触发一次且无内容。</summary>
    OneShot
}

/// <summary>
/// 解析器上下文信息，可选地注入到标签处理程序中。
/// </summary>
public class XmlTagContext
{
    public IReadOnlyList<TagInfo> CallChain { get; }

    /// <summary>触发本次处理的字符串（如果是因标点断句触发则不为 null）</summary>
    public string? Trigger { get; }

    /// <summary>当前标签的处理状态（开区间、内容、或闭区间）</summary>
    public TagStatus Status { get; }

    /// <summary>截至当前调用，该标签已积累的总内容（包含本次片段）</summary>
    public string FullContent { get; }

    /// <summary>本次触发所产生的片段内容</summary>
    public string ChunkContent { get; }

    public XmlTagContext(IReadOnlyList<TagInfo> stack, string? trigger, TagStatus status, string fullContent, string chunkContent)
    {
        CallChain = stack;
        Trigger = trigger;
        Status = status;
        FullContent = fullContent;
        ChunkContent = chunkContent;
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
