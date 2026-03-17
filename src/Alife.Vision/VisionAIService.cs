using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Net;

namespace Alife.Vision;

public class VisionAIService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;

    public VisionAIService(string endpoint, string modelId, string apiKey)
    {
        // 强制使用 HTTP 1.1 以兼容某些国产 API 提供商
        HttpClient httpClient = new(new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                RemoteCertificateValidationCallback = delegate { return true; }
            }
        }) {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: modelId,
            apiKey: apiKey,
            endpoint: new Uri(endpoint),
            httpClient: httpClient
        );

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <summary>
    /// 使用多模态模型描述图片内容
    /// </summary>
    public async Task<string> DescribeImageAsync(string imagePath, string prompt = "请描述这张图片的内容，用中文回答喵")
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException("找不到图片文件", imagePath);

        var history = new ChatHistory();
        var message = new ChatMessageContentItemCollection
        {
            new TextContent(prompt),
            new ImageContent(new ReadOnlyMemory<byte>(await File.ReadAllBytesAsync(imagePath)), "image/png")
        };

        history.AddUserMessage(message);

        var result = await _chatService.GetChatMessageContentAsync(history);
        return result.Content ?? "AI 没能给出有效的描述喵。";
    }
}
