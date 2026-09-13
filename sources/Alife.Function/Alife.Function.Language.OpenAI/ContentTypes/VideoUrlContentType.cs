using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>视频内容：协议序列化为 <c>video_url</c>。AI 通过 <c>LoadVideo</c> 查看，用 temp 参数选择临时/保留。</summary>
public sealed class VideoUrlContent(string url) : KernelContent
{
    /// <summary>网络地址或 data URI（本地文件读取后转base64）。</summary>
    public string Url { get; } = url;
}

public sealed class VideoUrlContentType : AlifeContentHandlerBase
{
    public override Type ContentType => typeof(VideoUrlContent);
    public override string DisplayName => "视频输入";
    public override string ProtocolTypeName => "video_url";

    public override JsonObject SerializeContent(KernelContent content)
    {
        VideoUrlContent video = (VideoUrlContent)content;
        return new JsonObject { ["type"] = "video_url", ["video_url"] = new JsonObject { ["url"] = video.Url } };
    }

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor)
    {
        if (IsEnabled(config) == false)
            return null;

        return BuildXmlFunction(
            "LoadVideo",
            null,
            [
                ("path", "视频本机路径或可直链访问的网络地址", "String"),
                ("temp", "临时分析并直接获取结果，默认false", "bool"),
            ],
            async (context, ct) => {
                VideoUrlContent video = new(await LoadVideoUrlAsync(context.Parameters["path"]));
                bool persistent = IsPersistentRequested(context);
                if (persistent && executor.IsPersistentAllowed(RegistrationKey!) == false)
                {
                    chatBot.Poke("保留模式未授权，仅可使用 temp=true 临时查看。");
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

    static async Task<string> LoadVideoUrlAsync(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.ToString();

        if (File.Exists(pathOrUrl) == false)
            throw new FileNotFoundException("视频不存在", pathOrUrl);

        byte[] data = await File.ReadAllBytesAsync(pathOrUrl);
        return $"data:{GetMimeType(pathOrUrl)};base64,{Convert.ToBase64String(data)}";
    }

    static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        _ => "application/octet-stream"
    };
}