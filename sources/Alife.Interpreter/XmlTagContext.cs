namespace Alife.Interpreter;

public readonly struct TagInfo
{
    public string Name { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; }

    public override string ToString() => Name;
}

public enum TagStatus
{
    Opening,
    Content,
    Closing,
    OneShot
}

public class XmlTagContext
{
    public IReadOnlyList<TagInfo> CallChain { get; }

    /// <summary>触发本次处理的断句字符串（非断句触发时为 null）</summary>
    public string? Trigger { get; }

    public TagStatus Status { get; }

    /// <summary>截至当前调用，该标签已积累的总内容（包含本次片段）</summary>
    public string FullContent { get; }

    public string ChunkContent { get; }

    public XmlTagContext(IReadOnlyList<TagInfo> stack, string? trigger, TagStatus status, string fullContent, string chunkContent)
    {
        CallChain = stack;
        Trigger = trigger;
        Status = status;
        FullContent = fullContent;
        ChunkContent = chunkContent;
    }

    public bool HasAncestor(string tagName)
        => CallChain.Any(t => t.Name == tagName);

    public string GetPath()
        => string.Join(" > ", CallChain.Select(t => t.Name));
}
