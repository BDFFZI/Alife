using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>视频内容：协议序列化为 <c>video_url</c>。AI 通过 <c>LookVideo</c> 查看，用 mode 参数选择临时/保留。</summary>
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

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor)
    {
        if (IsEnabled(config) == false)
            return null;

        return BuildXmlFunction(
            "LookVideo",
            null,
            [
                ("url", "可直链访问的网络地址", "String"),
                ("keep", "是否常驻上下文以便连续分析", "bool"),
            ],
            async (context, ct) => {
                VideoUrlContent video = new(RequireHttpUrl(context.Parameters["url"], "url"));
                bool persistent = IsPersistentRequested(context);
                if (persistent && executor.IsPersistentAllowed(RegistrationKey!) == false)
                {
                    chatBot.Poke("保留模式未授权，仅可使用 keep=false 临时查看。");
                    return;
                }
                if (persistent)
                {
                    await QueueContentAsync(chatBot, video, "将视频加入对话上下文");
                    chatBot.Poke("已上传");
                }
                else
                {
                    try
                    {
                        string result = await executor.CompleteWithContentAsync(chatBot, video, ct);
                        chatBot.Poke(string.IsNullOrWhiteSpace(result) ? "未能获取视频内容。" : result);
                    }
                    catch (Exception e)
                    {
                        chatBot.Poke($"视频查看失败：{e.Message}");
                    }
                }
            });
    }
}