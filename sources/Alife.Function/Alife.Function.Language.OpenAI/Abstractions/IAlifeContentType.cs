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
public interface IAlifeContentType
{
    /// <summary>该处理器支持的 SK 内容类型。</summary>
    Type ContentType { get; }

    /// <summary>配置与 UI 中用于勾选注册的标识；null 表示不可注册（如文本）。默认取 <see cref="ContentType"/> 的类型名。</summary>
    string? RegistrationKey { get; }

    /// <summary>UI 中显示的名称。</summary>
    string DisplayName { get; }

    /// <summary>协议 JSON 中对应的 content type 名（如 image_url），供用户比对模型服务商支持范围。</summary>
    string ProtocolTypeName { get; }

    /// <summary>将内容序列化为协议 JSON 块。</summary>
    JsonObject SerializeContent(KernelContent content);

    /// <summary>生成 AI 可调用的注册函数；返回 null 表示无需暴露给 AI（如文本）。executor 提供加入模式与临时式补全能力。</summary>
    XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor);
}