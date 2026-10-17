using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// 通过反射自动发现并持有当前程序集中所有 <see cref="AlifeContentRegistrar"/> 实现，
/// 并据此提供内容序列化与 AI 上传函数的汇总注册。
/// </summary>
public static class AlifeContentRegistry
{
    /// <summary>程序集中全部内容类型处理器（含新增脚本）。</summary>
    public static IReadOnlyList<AlifeContentRegistrar> Handlers { get; } = typeof(AlifeContentRegistry).Assembly.GetTypes()
        .Where(t => t.IsAbstract == false && typeof(AlifeContentRegistrar).IsAssignableFrom(t))
        .Select(t => (AlifeContentRegistrar)Activator.CreateInstance(t)!)
        .ToList();

    public static JsonObject SerializeContent(KernelContent content)
    {
        foreach (AlifeContentRegistrar handler in Handlers)
        {
            if (handler.ContentType.IsInstanceOfType(content))
                return handler.SerializeContent(content);
        }
        throw new NotSupportedException($"不支持的多模态内容类型 '{content.GetType().Name}'");
    }

    /// <summary>把各内容类型暴露的 AI 上传函数汇总注册到一个 XmlHandler。</summary>
    public static XmlHandler? BuildHandler(ChatBot chatBot,
        HashSet<string> enabledContentTypes,
        HashSet<string> enabledPersistentContentTypes)
    {
        List<XmlFunction> functions = new List<XmlFunction>();

        foreach (AlifeContentRegistrar type in Handlers)
        {
            if (type.SwitchId == null) //不可开关，永远生效
            {
                XmlFunction[] function = type.AttachedFunction(chatBot, true);
                functions.AddRange(function);
            }
            else
            {
                if (enabledContentTypes.Contains(type.SwitchId) == false)
                    continue;

                bool allowPersistent = enabledPersistentContentTypes.Contains(type.SwitchId);
                XmlFunction[] function = type.AttachedFunction(chatBot, allowPersistent);
                functions.AddRange(function);
            }
        }

        if (functions.Count == 0)
            return null;

        XmlHandler handler = new("MultimodalInput") {
            Description = "此服务函数可以让你直接通过自己的上下文来分析多模态内容，而不是通过外部模型。（注意：这无法用于给用户展示内容，因为上下文仅你自己可见）",
            Functions = functions
        };
        return handler;
    }
}