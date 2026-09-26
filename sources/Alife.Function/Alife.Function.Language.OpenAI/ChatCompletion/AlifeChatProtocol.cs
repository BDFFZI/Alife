using System;
using System.Text.Json.Nodes;
using Alife.Framework;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// Alife 多模态对话协议：负责把 SK 对话历史序列化为 OpenAI 兼容的多模态请求体，
/// 以及从响应中提取用量信息。不依赖 IO 与日志，是纯序列化层。
/// </summary>
/// <remarks>
/// 各内容类型的序列化与注册由 <see cref="AlifeContentRegistrar"/> 实现自行提供，
/// 通过 <see cref="AlifeContentRegistry"/> 自动发现，本类不持有具体类型的耦合。
/// </remarks>
public static class AlifeChatProtocol
{
    public static Uri CreateChatCompletionsUri(string endpoint)
    {
        string normalized = endpoint.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return new Uri(normalized);
        return new Uri(normalized + "/chat/completions");
    }

    public static JsonArray SerializeHistory(ChatHistory history)
    {
        JsonArray messages = new();
        foreach (ChatMessageContent message in history)
        {
            JsonArray content = new();
            foreach (KernelContent item in message.Items)
                content.Add(AlifeContentRegistry.SerializeContent(item));
            messages.Add(new JsonObject {
                ["role"] = message.Role.ToString().ToLowerInvariant(),
                ["content"] = content
            });
        }
        return messages;
    }

    public static TokenUsage ParseUsage(JsonObject usage)
    {
        return new TokenUsage() {
            Total = usage["total_tokens"]?.GetValue<int>() ?? 0,
            Input = usage["prompt_tokens"]?.GetValue<int>() ?? 0,
            Output = usage["completion_tokens"]?.GetValue<int>() ?? 0,
            Cached = usage["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0
        };
    }
}