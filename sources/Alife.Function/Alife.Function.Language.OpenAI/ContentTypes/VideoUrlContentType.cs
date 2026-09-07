using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>视频内容：协议序列化为 <c>video_url</c>；AI 可通过 <c>loadvideo</c> 上传 URL。</summary>
public sealed class VideoUrlContent(Uri url) : KernelContent
{
    public Uri Url { get; } = url;
}

public sealed class VideoUrlContentType : AlifeContentHandlerBase
{
    public override Type ContentType => typeof(VideoUrlContent);
    public override string DisplayName => "视频输入";
    public override string ProtocolTypeName => "video_url";

    public override JsonObject SerializeContent(KernelContent content)
    {
        VideoUrlContent video = (VideoUrlContent)content;
        return new JsonObject { ["type"] = "video_url", ["video_url"] = new JsonObject { ["url"] = video.Url.ToString() } };
    }

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config)
    {
        if (IsEnabled(config) == false)
            return null;
        return BuildXmlFunction(
            "loadvideo",
            null,
            "url",
            "可直链访问的网络地址",
            async (context, _) => {
                await LoadVideoAsync(chatBot, context.Parameters["url"]);
                chatBot.Poke(ContentType.Name + "内容");
            });
    }

    static async Task LoadVideoAsync(ChatBot chatBot, string url)
    {
        Uri uri = RequireHttpUrl(url, nameof(url));
        await QueueContentAsync(chatBot, new VideoUrlContent(uri), "将视频加入对话上下文");
    }
}