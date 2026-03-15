using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using System.Net;
using System.Net.Http;
using Alife.Plugins.Official.Components;

namespace Alife.Plugins.Official.Implement;

public class OpenAIChatServiceConfig : ICloneable
{
    public string endpoint = "https://api.deepseek.com/v1";
    public string modelId = "deepseek-chat";
    public string apiKey = "sk-7bf56492f14a40279cea280b49ae6de8";

    public object Clone()
    {
        return new OpenAIChatServiceConfig() {
            endpoint = endpoint,
            modelId = modelId,
            apiKey = apiKey
        };
    }
}

[Plugin(
    "OpenAI对话能力", "基于OpenAI协议的对话模型功能接入。",
    url: "https://www.deepseek.com/",
    configurationUIType: typeof(OpenAIChatServiceUI)
)]
public class OpenAIChatService : IPlugin, IConfigurable<OpenAIChatServiceConfig>
{
    public void Configure(OpenAIChatServiceConfig configuration)
    {
        this.configuration = configuration;
    }

    public Task AwakeAsync(IKernelBuilder builder, ChatHistoryAgentThread context)
    {
        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        HttpClient httpClient = new HttpClient(new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; } 
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        builder.AddOpenAIChatCompletion(
            endpoint: new Uri(configuration.endpoint),
            modelId: configuration.modelId,
            apiKey: configuration.apiKey,
            httpClient: httpClient
        );
        return Task.CompletedTask;
    }

    OpenAIChatServiceConfig configuration = null!;
}
