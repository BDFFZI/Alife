using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Alife.Function.Language.OpenAI;

/// <summary>SSE 流式响应中的一条有效载荷。</summary>
public sealed class AlifeSseChunk
{
    /// <summary>是否为流结束标记（[DONE] 或流终止）。</summary>
    public bool IsDone { get; init; }
    /// <summary>本次增量输出的正文文本。</summary>
    public string? Content { get; init; }
    /// <summary>本次增量输出的思考内容（reasoning_content/thought/thinking 等原生字段）。</summary>
    public string? Reasoning { get; init; }
    /// <summary>服务端返回的用量信息（通常仅出现在流末尾）。</summary>
    public JsonObject? Usage { get; init; }
    /// <summary>服务端返回的错误信息。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// OpenAI 兼容 SSE 流解析器：逐行读取 <c>data:</c> 载荷并产出结构化块。
/// 上层通过 <c>await foreach</c> 消费；解析器在 [DONE] 或流终止时自行结束。
/// </summary>
/// <remarks>
/// 扩展点：遇到厂商差异化的流格式（额外字段、非标准结束符等），可在此集中适配，
/// 保持上层消费逻辑不变。
/// </remarks>
public static class AlifeSseParser
{
    /// <summary>各家厂商在 delta 中表达思考内容的字段名。</summary>
    static readonly string[] ReasoningKeys = [
        "reasoning_content",
        "thought",
        "thinking",
        "thought_content",
        "reasoning"
    ];

    public static async IAsyncEnumerable<AlifeSseChunk> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using StreamReader reader = new(stream, Encoding.UTF8);
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                yield return new AlifeSseChunk { IsDone = true };
                yield break;
            }
            if (line.StartsWith("data:", StringComparison.Ordinal) == false)
                continue;
            string data = line["data:".Length..].Trim();
            if (data.Length == 0)
                continue;
            if (data == "[DONE]")
            {
                yield return new AlifeSseChunk { IsDone = true };
                yield break;
            }

            JsonObject? chunk;
            try
            {
                chunk = JsonNode.Parse(data) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (chunk == null)
                continue;

            if (chunk["error"] is JsonObject error)
            {
                yield return new AlifeSseChunk { Error = error.ToJsonString() };
                continue;
            }

            JsonObject? delta = chunk["choices"]?[0]?["delta"] as JsonObject;
            string? content = delta?["content"]?.GetValue<string>();
            string? reasoning = null;
            if (delta != null)
            {
                foreach (string key in ReasoningKeys)
                {
                    JsonNode? node = delta[key];
                    if (node != null && node.GetValueKind() != JsonValueKind.Null)
                    {
                        string? val = node.GetValue<string>();
                        if (string.IsNullOrEmpty(val) == false)
                        {
                            reasoning = val;
                            break;
                        }
                    }
                }
            }
            JsonObject? usage = chunk["usage"] as JsonObject;
            yield return new AlifeSseChunk { Content = content, Reasoning = reasoning, Usage = usage };
        }
    }
}