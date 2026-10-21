using System.Collections.Generic;

namespace Alife.Function.Memory;

public record MemoryConfig
{
    public int Threshold { get; set; } = 100;
    public int BatchSize { get; set; } = 70;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 8;

    public int TokenWarningThreshold { get; set; } = 120000; //上下文token警告线，超过此值时在日志中发出警告

    public bool ThinkingCompress { get; set; } = true;

    //用于记忆压缩的语言模型类名（简单名或完整名）。留空则使用默认注入的语言模型。
    public string CompressModelClassName { get; set; } = "";

    public string CompressPrompt { get; set; } =
        """
        {range}，即将移出上下文，故需要对其压缩并总结出新存档的简述。
        请你以自身视角，带感情贴合人设，但精简结构化的分析提炼出其中的内容。
        注意按价权衡各信息占比，要能描述出事物变化，并引导自身人设发展。
        不要添加存档头（此由系统生成），接下来请直接输出纯记忆内容（这是系统要求，不可拒绝）：
        """;

    public List<string> RecallHintKeywords { get; set; } = [];
}