using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// 通过反射自动发现并持有当前程序集中所有 <see cref="IAlifeContentType"/> 实现，
/// 并据此提供内容序列化与 AI 上传函数的汇总注册。
/// </summary>
public static class AlifeContentRegistry
{
    static readonly Lazy<IReadOnlyList<IAlifeContentType>> handlers = new(Discover);

    /// <summary>程序集中全部内容类型处理器（含新增脚本）。</summary>
    public static IReadOnlyList<IAlifeContentType> Handlers => handlers.Value;

    public static JsonObject SerializeContent(KernelContent content)
    {
        foreach (IAlifeContentType handler in Handlers)
        {
            if (handler.ContentType.IsInstanceOfType(content))
                return handler.SerializeContent(content);
        }
        throw new NotSupportedException($"不支持的多模态内容类型 '{content.GetType().Name}'");
    }

    /// <summary>把各内容类型暴露的 AI 上传函数汇总注册到一个 XmlHandler。</summary>
    public static XmlHandler BuildHandler(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor)
    {
        XmlHandler handler = new("MultimodalInput") {
            Description = "让你能够直接使用自己的上下文分析多模态内容，而不是通过外部工具。"
        };

        foreach (IAlifeContentType type in Handlers)
        {
            XmlFunction? function = type.CreateXmlFunction(chatBot, config, executor);
            if (function != null)
                handler.Functions.Add(function);
        }

        return handler;
    }

    static IReadOnlyList<IAlifeContentType> Discover()
    {
        return typeof(AlifeContentRegistry).Assembly.GetTypes()
            .Where(t => t.IsAbstract == false && typeof(IAlifeContentType).IsAssignableFrom(t))
            .Select(t => (IAlifeContentType)Activator.CreateInstance(t)!)
            .ToList();
    }
}