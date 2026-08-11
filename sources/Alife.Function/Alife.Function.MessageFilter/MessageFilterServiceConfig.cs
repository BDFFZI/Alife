using System.Collections.Generic;

namespace Alife.Function.MessageFilter;

public class MessageFilterServiceConfig
{
    public bool EnableTimestamp { get; set; } = true;
    public string MessageAppend { get; set; } =
        "(注意！看清消息来源和意图，不同场合用不同的标签，不要混用；有文档的一定要先查文档，学习标签用法后再进行回复；调用工具时不要编造结果，调完立即停下等待结果返回，然后再进行下一步；回复时要保持发言简洁，禁用旁白、emoji，但允许也建议多用系统支持的图片动作表情等)";
    public int InjectionInterval { get; set; } = 7;
    public string PokeAppend { get; set; } = "";
    public int MaxMessageLength { get; set; } = 10000;
    public List<RegexMessageReplyRuleConfig> MessageReplyRules { get; set; } = [];
}

public class RegexMessageReplyRuleConfig : RegexMessageReplyRule
{
    public bool Enabled { get; set; } = true;
}