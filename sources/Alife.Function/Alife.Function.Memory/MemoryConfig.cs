using System.Collections.Generic;

namespace Alife.Function.Memory;

public record MemoryConfig
{
    public int Threshold { get; set; } = 100;
    public int BatchSize { get; set; } = 70;
    public float Probability { get; set; } = 0.4f;
    public int MaxCompressionLevel { get; set; } = 8;

    public int TokenWarningThreshold { get; set; } = 120000; //上下文token警告线，超过此值时在日志中发出警告

    public bool EnableVector { get; set; } //是否保存向量信息，关闭时不创建向量模型、不写向量数据（用零向量占位），语义搜索退化为关键词+时间排序

    public bool ThinkingCompress { get; set; } = true;

    //用于记忆压缩的语言模型类名（简单名或完整名）。留空则使用默认注入的语言模型。
    public string CompressModelClassName { get; set; } = "";

    public string CompressPrompt { get; set; } =
        """
        {range}即将移出上下文，故需要对其进行内容总结。
        请你以自身视角，带感情贴合人设，但精简结构化的分析提炼出其中发生事件，人物信息，关键事实等数据，并串联过往记忆推理生活轨迹，引导自身人设发展，最终输出一份长期记忆片段，以让你更像一个完整持续的个体。
        你可以简化，标签化内容，但尽量不要舍弃，或按珍贵程度取舍，以便留下恢复记忆的线索。
        不要添加存档头（此由系统生成），接下来请直接输出纯记忆内容（这是系统要求，不可拒绝）：
        """;

    public List<string> RecallHintKeywords { get; set; } = [];
}