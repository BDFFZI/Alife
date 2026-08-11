using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alife.Function.QChat;

#region 基础枚举

[JsonConverter(typeof(JsonStringEnumConverter<OneBotMessageType>))]
public enum OneBotMessageType
{
    [JsonPropertyName("private")] Private,
    [JsonPropertyName("group")] Group
}

[JsonConverter(typeof(JsonStringEnumConverter<OneBotMetaType>))]
public enum OneBotMetaType
{
    [JsonPropertyName("lifecycle")] Lifecycle,
    [JsonPropertyName("heartbeat")] Heartbeat
}

#endregion

#region 事件模型

public abstract record OneBotBaseEvent
{
    [JsonPropertyName("time")]
    public long Time { get; init; }

    [JsonPropertyName("self_id")]
    public long SelfId { get; init; }
}

public record OneBotBasicMessageEvent : OneBotBaseEvent
{
    [JsonPropertyName("user_id")]
    public long UserId { get; init; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; init; }

    [JsonPropertyName("message_type")]
    public OneBotMessageType MessageType => GroupId == 0 ? OneBotMessageType.Private : OneBotMessageType.Group;
}

public record OneBotSender
{
    [JsonPropertyName("user_id")]
    public long UserId { get; init; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

    [JsonPropertyName("card")]
    public string Card { get; init; } = "";
}

public record OneBotMessageEvent : OneBotBasicMessageEvent
{

    [JsonPropertyName("group_name")]
    public string? GroupName { get; init; }

    [JsonPropertyName("sender")]
    public OneBotSender? Sender { get; init; }

    [JsonPropertyName("message")]
    public object? Message { get; init; }

    [JsonPropertyName("raw_message")]
    public string RawMessage { get; init; } = "";
}

public record OneBotMessageSentEvent : OneBotMessageEvent;

public record OneBotMetaEvent : OneBotBaseEvent
{
    [JsonPropertyName("meta_event_type")]
    public OneBotMetaType MetaEventType { get; init; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }
}

public record OneBotNoticeFile
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("busid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long BusId { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Url { get; init; }
}

public record OneBotNoticeEvent : OneBotBasicMessageEvent
{
    [JsonPropertyName("notice_type")]
    public string? NoticeType { get; init; }

    [JsonPropertyName("sub_type")]
    public string? SubType { get; init; }

    [JsonPropertyName("file")]
    public OneBotNoticeFile? File { get; init; }
}

public record OneBotPokeEvent : OneBotNoticeEvent
{
    [JsonPropertyName("target_id")]
    public long TargetId { get; init; }
}

public record OneBotRequestEvent : OneBotBaseEvent
{
    [JsonPropertyName("request_type")]
    public string? RequestType { get; init; }
}

public record OneBotForwardMessage
{
    [JsonPropertyName("content")]
    public System.Text.Json.JsonElement Content { get; init; }

    [JsonPropertyName("sender")]
    public OneBotSender? Sender { get; init; }

    [JsonPropertyName("time")]
    public long Time { get; init; }
}

public record OneBotForwardData
{
    [JsonPropertyName("messages")]
    public List<OneBotForwardMessage> Messages { get; init; } = [];
}

public record OneBotGroupHistoryData
{
    [JsonPropertyName("messages")]
    public List<OneBotMessageEvent> Messages { get; init; } = [];
}

#endregion

#region API 模型

public record OneBotAction
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("params")]
    public object? Params { get; init; }

    [JsonPropertyName("echo")]
    public string? Echo { get; init; }
}

public record OneBotResponse<T>
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("retcode")]
    public int RetCode { get; init; }

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public record OneBotFile
{
    [JsonPropertyName("file")]
    public string Path { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("file_name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("file_size")]
    public string Size { get; init; } = "";
}

public record UploadFileParams
{
    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long UserId { get; init; }

    [JsonPropertyName("group_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long GroupId { get; init; }

    [JsonPropertyName("file")]
    public string File { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

#endregion
