using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Language.OpenAI;

/// <summary>
/// 内容类型处理器抽象基类：提供把内容加入对话历史、校验 URL 以及构建
/// AI 上传函数的共享工具，具体内容的序列化与入史逻辑由各实现脚本自行提供。
/// </summary>
public abstract class AlifeContentHandlerBase : IAlifeContentType
{
    public abstract Type ContentType { get; }
    /// <summary>默认以协议 content type 名作为注册标识，与面板展示一致，用户按模型服务商支持范围勾选；同类协议的新变体（如 image_url2）天然拥有不同 id。</summary>
    public virtual string? RegistrationKey => ProtocolTypeName;
    public abstract string DisplayName { get; }
    public abstract string ProtocolTypeName { get; }
    public abstract JsonObject SerializeContent(KernelContent content);
    public abstract XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor);

    /// <summary>按配置中的 <see cref="OpenAILanguageModelConfig.enabledContentTypes"/> 判断本类型是否启用注册。</summary>
    protected bool IsEnabled(OpenAILanguageModelConfig config)
    {
        return string.IsNullOrEmpty(RegistrationKey) == false && config.enabledContentTypes.Contains(RegistrationKey);
    }

    /// <summary>解析函数参数中的 keep（bool），判断 AI 是否请求常驻上下文。</summary>
    protected static bool IsPersistentRequested(XmlContext context)
    {
        return context.Parameters.TryGetValue("keep", out string? keep) &&
            (keep.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             keep.Equals("1", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把内容加入对话历史。AI 上传函数在模型流式期间被后台执行，使用异步编辑不会死锁。</summary>
    protected static Task QueueContentAsync(ChatBot chatBot, KernelContent content, string reason)
    {
        return chatBot.EditChatHistoryAsync(thread => {
            ChatMessageContent chatMessageContent = new(AuthorRole.User, [content]) {
                Content = content.GetType().Name
            };
            thread.ChatHistory.Add(chatMessageContent);
            return Task.CompletedTask;
        }, reason);
    }

    /// <summary>校验并解析模型服务可访问的 http(s) 地址。</summary>
    protected static Uri RequireHttpUrl(string url, string argumentName)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("必须是模型服务可访问的 http(s) 地址。", argumentName);
        return uri;
    }

/// <summary>构建一个参数化的 AI 可调用 XmlFunction。</summary>
    protected static XmlFunction BuildXmlFunction(
        string name,
        string? description,
        IEnumerable<(string Name, string Description, string Type)> parameters,
        Func<XmlContext, CancellationToken, Task> invoker)
    {
        return new XmlFunction {
            Name = name,
            Description = description,
            Mode = FunctionMode.OneShot,
            Parameters = parameters
                .Select(p => new XmlParameter { Name = p.Name, Type = p.Type, Description = p.Description })
                .ToList(),
            Invoker = invoker
        };
    }
}