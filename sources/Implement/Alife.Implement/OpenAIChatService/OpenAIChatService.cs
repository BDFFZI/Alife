using Alife.Framework;
using Microsoft.SemanticKernel;
using System.Net;
using Alife.Plugins.Official.Components;

namespace Alife.Implement;

public class OpenAIChatServiceConfig : ICloneable
{
    public string endpoint = "";
    public string modelId = "";
    public string apiKey = "";

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
    "框架-OpenAI对话能力", "基于OpenAI协议的对话模型功能接入。",
    url: "https://www.deepseek.com/",
    configurationUIType: typeof(OpenAIChatServiceUI)
)]
public class OpenAIChatService : Plugin, IConfigurable<OpenAIChatServiceConfig>
{
    public void Configure(OpenAIChatServiceConfig configuration)
    {
        this.configuration = configuration;
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        // 强制使用 HTTP 1.1 以解决某些提供者（如 DeepSeek）在流式传输时可能出现的 HttpIOException
        HttpClient httpClient = new(new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        context.kernelBuilder.AddOpenAIChatCompletion(
            endpoint: new Uri(configuration.endpoint),
            modelId: configuration.modelId,
            apiKey: configuration.apiKey,
            httpClient: httpClient
        );
        return Task.CompletedTask;
    }

    OpenAIChatServiceConfig configuration = null!;
}
