using System;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// Alife 多模态协议内容类型处理器：一种 Content 类型对应一个实现脚本，
/// 自带协议序列化与 AI 调用注册函数。
/// </summary>
/// <remarks>
/// 新增内容类型只需新建脚本实现本接口，即可被 <see cref="AlifeContentRegistry"/>
/// 自动发现并参与序列化与函数注册，无需改动任何其他代码。
/// </remarks>
public abstract class AlifeContentRegistrar
{
    /// <summary>
    /// 当有 id 时将可以被显示在面板上进行开关，否则始终启用
    /// </summary>
    public virtual string? SwitchId => DisplayName;

    /// <summary>显示在 UI 中的名称。</summary>
    public abstract string DisplayName { get; }

    /// <summary>显示在 UI 中的 Json 字段协议名（如 image_url），用于让用户比对模型是否支持。</summary>
    public abstract string ProtocolTypeName { get; }

    /// <summary>对应到 SK 中的内容类型。</summary>
    public abstract Type ContentType { get; }

    /// <summary>将 SK 内容序列化为协议 JSON 块。</summary>
    public abstract JsonObject SerializeContent(KernelContent content);

    /// <summary>提供的额外的函数，用于让 AI 自主上传多模态内容</summary>
    public virtual XmlFunction[] AttachedFunction(ChatBot chatBot, bool allowPersistent)
    {
        return [];
    }
}