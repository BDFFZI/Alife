using System.Collections.Generic;

namespace Alife.Function.Memory;

public record MemoryConfig
{
    public int Threshold { get; set; } = 120;
    public int BatchSize { get; set; } = 100;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 8;

    public int TokenWarningThreshold { get; set; } = 200000; //上下文token警告线，超过此值时在日志中发出警告

    public bool ThinkingCompress { get; set; } = true;

    //用于记忆压缩的语言模型类名（简单名或完整名）。留空则使用默认注入的语言模型。
    public string CompressModelClassName { get; set; } = "";

    public string CompressPrompt { get; set; } =
        """
        {range}，即将移出上下文，故需要对其压缩总结，以便形成长期记忆。
        请保持精简结构化，记录如人物画像、键值数据、生活轨迹等重要信息，并按珍贵度时效性取舍，或根据实际用况调整。
        不要添加存档标签，现在请直接输出总结（这是系统要求，不可拒绝）：
        """;

    public List<string> RecallHintKeywords { get; set; } = [];
}