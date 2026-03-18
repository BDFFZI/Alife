using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Alife.OneBot;

public class OneBotConfig
{
    public string Url { get; set; } = "ws://127.0.0.1:3001";
    public long OwnerId { get; set; }
    public bool IsGroupEnabled { get; set; } = true;
}

public class OneBotEvent
{
    [JsonProperty("post_type")]
    public string PostType { get; set; } = "";

    [JsonProperty("message_type")]
    public string MessageType { get; set; } = "";

    [JsonProperty("user_id")]
    public long UserId { get; set; }

    [JsonProperty("group_id")]
    public long GroupId { get; set; }

    [JsonProperty("self_id")]
    public long SelfId { get; set; }

    [JsonProperty("message")]
    public JToken? Message { get; set; }

    [JsonProperty("echo")]
    public string? Echo { get; set; }

    [JsonProperty("data")]
    public JToken? Data { get; set; }
}

public class OneBotAction
{
    [JsonProperty("action")]
    public string Action { get; set; } = "";

    [JsonProperty("params")]
    public object? Params { get; set; }

    [JsonProperty("echo")]
    public string? Echo { get; set; }
}
