using System;
using System.Collections.Generic;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 一次模型调用（一轮对话）的完整记录。持久化为按月 JSONL 的一行。
/// 时间统一存北京时间（DateTimeOffset，+08:00），计费与按日分组都基于它。
/// </summary>
public sealed class ChatRecord
{
    public string Id { get; set; } = "";

    /// <summary>对话发生时间（北京时间）。</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>计价所用的价格配置名（如 deepseek-flash）。</summary>
    public string PriceProfile { get; set; } = "";

    public int InputTotal { get; set; }
    public int InputCached { get; set; }
    public int OutputTotal { get; set; }

    /// <summary>本轮费用（元）。</summary>
    public double Cost { get; set; }

    /// <summary>用户消息原文（给 AI 的输入）。</summary>
    public string UserMessage { get; set; } = "";

    /// <summary>AI 正文输出。</summary>
    public string AiMessage { get; set; } = "";

    /// <summary>AI 思考内容（由模型原生字段拆出，与正文分离）。</summary>
    public string AiThinking { get; set; } = "";

    /// <summary>用户侧标签及其字节数（每次出现一条）。</summary>
    public List<MarkupHit> UserTags { get; set; } = new();

    /// <summary>AI 侧函数及其字节数（每次出现一条）。</summary>
    public List<MarkupHit> AiFunctions { get; set; } = new();
}
