using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

/// <summary>
/// OneBot CQ 码与富文本处理工具
/// </summary>
public static class OneBotSegment
{
    public static string GetSpeakerTag(this OneBotBasicMessageEvent basicMessage)
    {
        string sayerLabel = basicMessage is OneBotMessageEvent messageEvent
            ? $"{basicMessage.UserId}({messageEvent.Sender?.Nickname})"
            : $"{basicMessage.UserId}";
        return $"[{sayerLabel}]";
    }
    public static string? GetPrivateName(this OneBotBasicMessageEvent basicMessage)
    {
        if (basicMessage is OneBotMessageEvent messageEvent)
            return messageEvent.Sender?.Nickname;
        return null;
    }
    public static string? GetGroupName(this OneBotBasicMessageEvent basicMessage)
    {
        if (basicMessage is OneBotMessageEvent messageEvent)
            return messageEvent.GroupName;
        return null;
    }

    /// <summary>
    /// 将消息转换为 AI 友好的可读文本（处理回复、@、图片、表情等）。
    /// </summary>
    public static async Task<string> GetReadableMessage(this OneBotMessageEvent messageEvent, OneBotClient oneBotClient)
    {
        string content = string.IsNullOrEmpty(messageEvent.RawMessage) && messageEvent.Message is System.Text.Json.JsonElement elem
            ? elem.ToCQString()
            : messageEvent.RawMessage;
        content = FilterFace(content);
        content = FilterAt(content);
        content = await FilterReply(content, oneBotClient);
        content = await FilterFile(content, messageEvent.GroupId, oneBotClient);
        content = FilterForward(content);
        content = FilterImage(content);
        return content;
    }
    /// <summary>
    /// 处理转发消息中的内容（处理嵌套转发、图片、表情等）。
    /// </summary>
    public static string GetReadableForwardContent(System.Text.Json.JsonElement content, OneBotClient oneBotClient)
    {
        string text = content.ToCQString();
        text = FilterFace(text);
        text = FilterAt(text);
        text = FilterForward(text, true);
        text = FilterImage(text);
        return text;
    }

    /// <summary>
    /// 将 JsonElement (可能是 string 或 segment array) 转换为 CQ 码字符串。
    /// </summary>
    public static string ToCQString(this System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.String)
            return element.GetString() ?? "";

        if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            StringBuilder sb = new();
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                if (item.TryGetProperty("type", out System.Text.Json.JsonElement typeElem) == false) continue;
                string type = typeElem.GetString() ?? "";
                if (item.TryGetProperty("data", out System.Text.Json.JsonElement dataElem) == false) continue;

                if (type == "text")
                {
                    if (dataElem.TryGetProperty("text", out System.Text.Json.JsonElement textElem))
                        sb.Append(textElem.GetString());
                }
                else
                {
                    sb.Append($"[CQ:{type}");
                    foreach (System.Text.Json.JsonProperty prop in dataElem.EnumerateObject())
                    {
                        string val = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.GetRawText();
                        sb.Append($",{prop.Name}={val}");
                    }
                    sb.Append(']');
                }
            }
            return sb.ToString();
        }

        return "";
    }
    /// <summary>
    /// 将转发消息列表格式化为 AI 可读的 Markdown 文本。
    /// </summary>
    public static string FormatForwardList(string forwardId, List<OneBotForwardMessage> messages, OneBotClient oneBotClient)
    {
        StringBuilder sb = new();
        sb.AppendLine($"# 转发消息内容 (ID: {forwardId})");
        foreach (OneBotForwardMessage msg in messages)
        {
            string readableContent = GetReadableForwardContent(msg.Content, oneBotClient);
            sb.AppendLine($"## {msg.Sender?.UserId}({msg.Sender?.Nickname})：");
            sb.AppendLine(readableContent);
        }
        return sb.ToString();
    }
    public static string FilterFace(string text)
    {
        return Regex.Replace(text, @"\[CQ:face,.*?id=(?<id>\d+).*?\]", "[表情: ${id}]");
    }
    public static string FilterForward(string text, bool isNested = false)
    {
        string label = isNested ? "嵌套转发消息" : "转发消息";
        return Regex.Replace(text, @"\[CQ:forward,.*?id=(?<id>[^,\]]+).*?\]", $"[{label}: ${{id}}]");
    }
    public static string FilterAt(string text)
    {
        return Regex.Replace(text, @"\[CQ:at,.*?qq=(?<qq>\d+)[^\]]*\]", "@${qq}");
    }
    public static async Task<string> FilterReply(string text, OneBotClient client)
    {
        var matches = Regex.Matches(text, @"\[CQ:reply,.*?id=(?<id>-?\d+)[^\]]*\]");
        foreach (Match match in matches)
        {
            if (long.TryParse(match.Groups["id"].Value, out long replyId))
            {
                OneBotMessageEvent? quoted = await client.GetMessage(replyId);
                string quotedInfo = quoted != null
                    ? $"[对“{quoted.UserId}：{await quoted.GetReadableMessage(client)}”的回复]"
                    : "[对其他消息的回复]";
                text = text.Replace(match.Value, quotedInfo);
            }
        }
        return text;
    }
    public static string FilterImage(string text)
    {
        text = Regex.Replace(text, @"\[CQ:image,.*?url=(?<url>http[s]?://[^,\]]+).*?\]", "[图片: ${url}]");
        text = Regex.Replace(text, @"\[CQ:image,.*?file=(?<file>[^,\]]+).*?\]", "[图片: ${file}]");
        text = Regex.Replace(text, @"\[CQ:image[^\]]*\]", "[图片]");
        return text;
    }
    public static async Task<string> FilterFile(string text, long groupId, OneBotClient client)
    {
        var matches = Regex.Matches(text, @"\[CQ:file,.*?\]");
        foreach (Match match in matches)
        {
            string segment = match.Value;
            string fileId = Regex.Match(segment, @"file_id=(?<id>[^,\]]+)").Groups["id"].Value;
            string fileName = Regex.Match(segment, @"file=(?<name>[^,\]]+)").Groups["name"].Value;

            if (string.IsNullOrEmpty(fileId)) continue;

            OneBotFile? fileInfo = groupId != 0
                ? await client.GetGroupFileUrl(groupId, fileId)
                : await client.GetPrivateFileUrl(fileId);

            string info = fileInfo != null
                ? $"[文件: {fileName}, 大小: {fileInfo.Size}b, 下载地址: {fileInfo.Url}]"
                : $"[文件: {fileName}]";

            text = text.Replace(segment, info);
        }
        return text;
    }


    /// <summary>
    /// 提取消息中的 @ ID（如果存在）。
    /// </summary>
    public static long? GetAtID(this OneBotMessageEvent message) => GetAtID(message.RawMessage);
    public static long? GetAtID(string rawMessage)
    {
        if (string.IsNullOrEmpty(rawMessage)) return null;
        Match match = Regex.Match(rawMessage, @"\[CQ:at,.*?qq=(?<id>-?\d+)");
        if (match.Success == false) return null;
        if (long.TryParse(match.Groups["id"].Value, out long id))
            return id;
        return null;
    }

    /// <summary>
    /// 提取消息中的回复消息 ID（如果存在）。
    /// </summary>
    public static long? GetReplyId(this OneBotMessageEvent message) => GetReplyId(message.RawMessage);
    public static long? GetReplyId(string rawMessage)
    {
        if (string.IsNullOrEmpty(rawMessage)) return null;
        Match match = Regex.Match(rawMessage, @"\[CQ:reply,.*?id=(?<id>-?\d+)");
        if (match.Success == false) return null;
        if (long.TryParse(match.Groups["id"].Value, out long id))
            return id;
        return null;
    }

    public static bool HasFile(this OneBotMessageEvent message) => message.RawMessage.Contains("[CQ:file");

    public static string? GetFileId(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"\[CQ:file,.*?file_id=(?<id>[^,\]]+)");
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static string? GetFileName(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"file=(?<name>[^,\]]+)");
        return match.Success ? match.Groups["name"].Value : null;
    }

    public static long? GetFileSize(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"file_size=(?<size>\d+)");
        if (match.Success && long.TryParse(match.Groups["size"].Value, out long result))
            return result;
        return null;
    }

    /// <summary>
    /// 提取消息中的转发消息 ID（如果存在）。
    /// </summary>
    public static string? GetForwardId(this OneBotMessageEvent message)
    {
        Match match = Regex.Match(message.RawMessage, @"\[CQ:forward,.*?id=(?<id>[^,\]]+)");
        return match.Success ? match.Groups["id"].Value : null;
    }
    /// <summary>
    /// 移除所有 CQ 码，保留纯文本。
    /// </summary>
    public static string GetPlainText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return Regex.Replace(text, @"\[CQ:.*?\]", "").Trim();
    }
}