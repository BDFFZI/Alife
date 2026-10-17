using System.Collections.Generic;

namespace Alife.Function.Auditory;

public class AuditoryServiceConfig
{
    public string? PushToTalkKey { get; set; }
    public List<string> ReplyRestrictedWords { get; set; } = ["<speak>"];
}