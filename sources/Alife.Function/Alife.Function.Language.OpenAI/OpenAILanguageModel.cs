using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.SemanticKernel;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace Alife.Function.Language.OpenAI;

[Module(
    "OpenAI语言模型",
    "接入与OpenAI协议兼容的语言模型，实现最基本的文本对话功能。",
    defaultCategory: "Alife 官方/模型接入/语言模型",
    editorUI: typeof(OpenAILanguageModelUI)
)]
public class OpenAILanguageModel(
    StorageSystem storageSystem,
    ILogger<OpenAILanguageModel> logger) :
    ChatBehaviour,
    ILanguageModel,
    IConfigurable<OpenAILanguageModelConfig>,
    IThinkingAbility
{
    public OpenAILanguageModelConfig Configuration { get; set; } = null!;
    public OccupationNotepad ThinkingRequest { get; } = new();

    public async Task<string> ChatStreamingAsync(
        ChatHistoryAgentThread chatHistoryAgentThread,
        Action<string>? textReceived = null,
        Action<string>? thinkReceived = null,
        Action<TokenUsage>? tokenUsed = null,
        Action<Exception>? exceptionThrow = null,
        CancellationToken cancellationToken = default)
    {
        StringBuilder nonThinkingContent = new(); //用于存储不含思考过程的最终回复
        ChatCompletionAgent agent = Configuration.defaultThinking || ThinkingRequest.IsOccupied
            ? chatCompletionAgent
            : chatCompletionAgentNotThinking;

        try
        {
            await foreach (AgentResponseItem<StreamingChatMessageContent> chatMessage in agent.InvokeStreamingAsync(
                               chatHistoryAgentThread, cancellationToken: cancellationToken))
            {
                string? content = chatMessage.Message.Content;
                if (content != null)
                {
                    //前置报文会对思考内容进行特殊处理，以便兼容思考模式
                    if (content.StartsWith(OpenAICompatibleHandler.ThinkContentPrefix))
                    {
                        string reasoningPart = content.Substring(OpenAICompatibleHandler.ThinkContentPrefix.Length);
                        thinkReceived?.Invoke(reasoningPart);
                    }
                    else
                    {
                        nonThinkingContent.Append(content);
                        textReceived?.Invoke(content);
                    }
                }

                var metaData = chatMessage.Message.Metadata;
                if (metaData != null)
                {
                    // 尝试从元数据中提取思考过程 (支持原生支持此字段的 SDK)
                    if (metaData.TryGetValue("ReasoningContent", out object? reasoning) ||
                        metaData.TryGetValue("reasoning_content", out reasoning))
                    {
                        string? reasoningStr = reasoning?.ToString();
                        if (string.IsNullOrEmpty(reasoningStr) == false)
                            thinkReceived?.Invoke(reasoningStr);
                    }

                    if (metaData.TryGetValue("Usage", out object? usage))
                    {
                        if (usage is ChatTokenUsage chatTokenUsage)
                        {
                            tokenUsed?.Invoke(new TokenUsage() {
                                Total = chatTokenUsage.TotalTokenCount,
                                Input = chatTokenUsage.InputTokenCount,
                                Output = chatTokenUsage.OutputTokenCount,
                                Cached = chatTokenUsage.InputTokenDetails?.CachedTokenCount ?? 0
                            });
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            exceptionThrow?.Invoke(e);
        }

        //受 SK 框架限制，思考只能存储在消息块中，所以需要额外的步骤修正内容。
        string aiMessage = nonThinkingContent.ToString();
        ChatMessageContent lastMsg = chatHistoryAgentThread.ChatHistory[^1];
        if (lastMsg.Role == AuthorRole.Assistant && (lastMsg.Content?.Contains(OpenAICompatibleHandler.ThinkContentPrefix) ?? false))
            lastMsg.Content = aiMessage;

        return aiMessage;
    }


    ChatCompletionAgent chatCompletionAgent = null!;
    ChatCompletionAgent chatCompletionAgentNotThinking = null!;

    [Experimental("SKEXP0010")]
    protected override Task OnAwake()
    {
        if (string.IsNullOrEmpty(Configuration.endpoint))
            Configuration.endpoint = storageSystem.GetProperty("endpoint", string.Empty)!;
        if (string.IsNullOrEmpty(Configuration.apiKey))
            Configuration.apiKey = storageSystem.GetProperty("apiKey", string.Empty)!;
        if (string.IsNullOrEmpty(Configuration.modelId))
            Configuration.modelId = storageSystem.GetProperty("modelId", string.Empty)!;

        IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
        RegisterChatCompletion(kernelBuilder);
        Kernel kernelService = kernelBuilder.Build();

        chatCompletionAgent = new() {
            Kernel = kernelService,
            Arguments = new KernelArguments(ProvidePromptExecutionSettings(true)),
        };
        chatCompletionAgentNotThinking = new() {
            Kernel = kernelService,
            Arguments = new KernelArguments(ProvidePromptExecutionSettings(false)),
        };

        return Task.CompletedTask;
    }


    void RegisterChatCompletion(IKernelBuilder kernelBuilder)
    {
        if (string.IsNullOrWhiteSpace(Configuration.apiKey))
            throw new Exception("语言模型的key为空，请检查你的“OpenAI语言模型”插件配置是否正确。");

        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        SocketsHttpHandler handler = new() {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate {
                    return true;
                }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        // 使用通用处理器拦截并破解所有 OpenAI 兼容协议的思考过程字段
        OpenAICompatibleHandler reasoningHandler = new(handler);

        HttpClient httpClient = new(reasoningHandler) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        if (!string.IsNullOrWhiteSpace(Configuration.extraHeaders))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(Configuration.extraHeaders);
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "解析自定义请求头失败");
            }
        }

        kernelBuilder.AddOpenAIChatCompletion(
            endpoint: new Uri(Configuration.endpoint),
            modelId: Configuration.modelId,
            apiKey: Configuration.apiKey,
            httpClient: httpClient
        );
    }

    [Experimental("SKEXP0010")]
    PromptExecutionSettings ProvidePromptExecutionSettings(bool thinking)
    {
        OpenAIPromptExecutionSettings settings = new();

        if (thinking)
        {
            if (string.IsNullOrEmpty(Configuration.reasoningEffort) == false)
                settings.ReasoningEffort = Configuration.reasoningEffort;
        }
        else
        {
            settings.ReasoningEffort = null;
        }

        settings.ExtraBody = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(Configuration.extraBody))
        {
            try
            {
                var bodyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    thinking ? Configuration.extraBody : Configuration.extraBodyNotThinking);
                if (bodyDict != null)
                {
                    foreach (var kvp in bodyDict)
                    {
                        settings.ExtraBody[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "解析自定义请求体失败");
            }
        }

        return settings;
    }
}