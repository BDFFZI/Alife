using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.TokenStatistics;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

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
    ILanguageModel, ITokenUser,
    IConfigurable<OpenAILanguageModelConfig>
{
    public event Action<TokenUsageContext>? TokenUsed;

    public OpenAILanguageModelConfig Configuration { get; set; } = null!;

    public OccupationNotepad GetThinkingRequester()
    {
        return thinkingRequester;
    }

    public async Task<ChatResult> ChatStreamingAsync(
        ChatHistoryAgentThread chatHistoryAgentThread,
        Action<string>? textReceived = null,
        Action<string>? thinkReceived = null,
        CancellationToken cancellationToken = default)
    {
        StringBuilder aiMessageBuilder = new();
        StringBuilder aiThinkingBuilder = new();
        Exception? exception = null;
        TokenUsage tokenUsageBuilder = new();

        //计算用户消息
        string userMessage;
        {
            int userMessageIndex = 0;
            for (int i = chatHistoryAgentThread.ChatHistory.Count - 1; i >= 0; i--)
            {
                ChatMessageContent content = chatHistoryAgentThread.ChatHistory[i];
                if (content.Role != AuthorRole.User)
                {
                    userMessageIndex = i + 1;
                    break;
                }
            }
            for (int i = userMessageIndex; i < chatHistoryAgentThread.ChatHistory.Count; i++)
            {
                ChatMessageContent content = chatHistoryAgentThread.ChatHistory[i];
                aiMessageBuilder.AppendLine(content.Content);
            }

            userMessage = aiMessageBuilder.ToString();
            aiMessageBuilder.Clear();
        }

        try
        {
            bool thinking = GetThinkingRequester().IsOccupied;

            using HttpRequestMessage request = BuildRequest(chatHistoryAgentThread.ChatHistory, thinking);
            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode == false)
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"语言模型返回 {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}");
            }

            await foreach (AlifeSseChunk chunk in AlifeSseParser.ParseAsync(
                               await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken))
            {
                if (chunk.Error != null)
                    throw new HttpRequestException($"语言模型流式返回错误: {chunk.Error}");

                //思考与正文由解析器直接从原生字段拆出，各自分发
                if (chunk.Reasoning != null)
                {
                    aiThinkingBuilder.Append(chunk.Reasoning);
                    thinkReceived?.Invoke(chunk.Reasoning);
                }

                if (chunk.Content != null)
                {
                    aiMessageBuilder.Append(chunk.Content);
                    textReceived?.Invoke(chunk.Content);
                }

                if (chunk.Usage != null)
                    tokenUsageBuilder = AlifeChatProtocol.ParseUsage(chunk.Usage);
            }
        }
        catch (Exception e)
        {
            exception = e;
        }

        ChatResult chatResult = new() {
            AIMessage = aiMessageBuilder.ToString(),
            AIThinking = aiThinkingBuilder.ToString(),
            Exception = exception
        };

        //把 AI 回复写入对话历史，供下一轮对话继续参考（不含思考内容）；
        //取消/异常时也保留已输出的部分内容（与旧实现一致），仅当完全没有输出时不入史。
        if (string.IsNullOrEmpty(chatResult.AIMessage) == false)
            chatHistoryAgentThread.ChatHistory.AddAssistantMessage(chatResult.AIMessage);

        TokenUsageContext tokenUsageContext = new() {
            AIMessage = chatResult.AIMessage,
            AIThinking = chatResult.AIThinking,
            UserMessage = userMessage,
            TokenUsage = tokenUsageBuilder
        };
        TokenUsed?.Invoke(tokenUsageContext);

        return chatResult;
    }

    /// <summary>临时式补全：复用主线程，在锁内临时追加媒体为最新用户消息，调用 ChatStreamingAsync 后移除临时消息，不污染主历史。</summary>
    public async Task ChatWithTempContentAsync(
        ChatBot chatBot, KernelContent content, CancellationToken cancellationToken = default)
    {
        ChatMessageContent chatMessageContent = new ChatMessageContent(AuthorRole.User, [content]);
        await chatBot.ChatAsync(chatMessageContent, false);
        await chatBot.EditChatHistoryAsync(thread => {
            thread.ChatHistory.Remove(chatMessageContent);
            return Task.CompletedTask;
        }, "移除临时多模态资源");
    }


    readonly OccupationNotepad thinkingRequester = new();
    HttpClient httpClient = null!;

    protected override async Task OnAwake()
    {
        if (string.IsNullOrEmpty(Configuration.endpoint))
            Configuration.endpoint = storageSystem.GetProperty("endpoint", string.Empty)!;
        if (string.IsNullOrEmpty(Configuration.apiKey))
            Configuration.apiKey = storageSystem.GetProperty("apiKey", string.Empty)!;
        if (string.IsNullOrEmpty(Configuration.modelId))
            Configuration.modelId = storageSystem.GetProperty("modelId", string.Empty)!;

        if (string.IsNullOrWhiteSpace(Configuration.apiKey))
            throw new Exception("语言模型的key为空，请检查你的“OpenAI语言模型”插件配置是否正确。");

        //构建专用httpClient
        {
            // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
            SocketsHttpHandler httpHandler = new() {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                    RemoteCertificateValidationCallback = delegate {
                        return true;
                    }
                },
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            // 直接使用原生处理器，思考/正文由 AlifeSseParser 原生字段解析
            httpClient = new HttpClient(httpHandler) {
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            //附加请求头
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
        }

        //默认思考功能
        if (Configuration.defaultThinking)
            thinkingRequester.Rent("默认思考");

        //多模态功能
        if (Configuration.enabledContentTypes.Count > 0)
        {
            XmlFunctionCaller functionCaller = (XmlFunctionCaller)await ChatActivity.Container
                .RequireInstance(typeof(XmlFunctionCaller), true);

            XmlHandler? handler = AlifeContentRegistry.BuildHandler(ChatBot,
                Configuration.enabledContentTypes,
                Configuration.enabledPersistentContentTypes
            );
            if (handler != null)
                functionCaller.RegisterHandler(handler, DocumentMode.Explicit, DestroyCancellationToken);
        }

        ChatBot.ChatFinished += OnChatFinished;
    }

    protected override Task OnDestroy()
    {
        ChatBot.ChatFinished -= OnChatFinished;

        httpClient.Dispose();
        return Task.CompletedTask;
    }

    void OnChatFinished(ChatContext obj)
    {
        AlifeContentQueue.Flash(ChatBot);
    }

    HttpRequestMessage BuildRequest(ChatHistory history, bool thinking)
    {
        JsonObject payload = new() {
            ["model"] = Configuration.modelId,
            ["messages"] = AlifeChatProtocol.SerializeHistory(history),
            ["stream"] = true,
            ["temperature"] = Configuration.temperature,
        };

        if (thinking && string.IsNullOrEmpty(Configuration.reasoningEffort) == false)
            payload["reasoning_effort"] = Configuration.reasoningEffort;

        string extraBody = thinking ? Configuration.extraBody : Configuration.extraBodyNotThinking;
        if (!string.IsNullOrWhiteSpace(extraBody))
        {
            try
            {
                var bodyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(extraBody);
                if (bodyDict != null)
                {
                    foreach (var kvp in bodyDict)
                    {
                        payload[kvp.Key] = JsonSerializer.SerializeToNode(kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "解析自定义请求体失败");
            }
        }

        Uri chatCompletionsUri = AlifeChatProtocol.CreateChatCompletionsUri(Configuration.endpoint);
        HttpRequestMessage request = new(HttpMethod.Post, chatCompletionsUri) {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Configuration.apiKey);
        return request;
    }
}