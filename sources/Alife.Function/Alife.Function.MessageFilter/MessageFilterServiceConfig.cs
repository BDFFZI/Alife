using System.Collections.Generic;

namespace Alife.Function.MessageFilter;

public class MessageFilterServiceConfig
{
    public bool EnableTimestamp { get; set; } = true;
    public int TimestampInterval { get; set; } = 40;
    public string MessageAppend { get; set; } = "(回复时请保持发言简洁，禁用旁白、emoji；积极使用系统提供的图片、动作、表情等，来让对话显得生动有趣)";
    public int InjectionInterval { get; set; } = 7;
    public string PokeAppend { get; set; } = "";
    public int MaxMessageLength { get; set; } = 10000;
    public List<RegexMessageReplyRuleConfig> MessageReplyRules { get; set; } = [];
}

public class RegexMessageReplyRuleConfig : RegexMessageReplyRule
{
    public bool Enabled { get; set; } = true;
}