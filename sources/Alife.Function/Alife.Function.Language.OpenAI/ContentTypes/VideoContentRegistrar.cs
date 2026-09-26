using System;
using System.IO;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>视频内容：协议序列化为 <c>video_url</c>。AI 通过 <c>LoadVideo</c> 查看，用 temp 参数选择临时/保留。</summary>
public sealed class VideoUrlContent(string url) : KernelContent
{
    /// <summary>data URI（网络地址在上传前已下载固化为 base64）。</summary>
    public string Url { get; } = url;
}

public sealed class VideoContentRegistrar : AlifeContentRegistrar
{
    public override string DisplayName => "视频输入";
    public override string ProtocolTypeName => "video_url";
    public override Type ContentType => typeof(VideoUrlContent);

    public override JsonObject SerializeContent(KernelContent content)
    {
        VideoUrlContent video = (VideoUrlContent)content;
        return new JsonObject {
            ["type"] = "video_url",
            ["video_url"] = new JsonObject {
                ["url"] = video.Url
            }
        };
    }

    public override XmlFunction[] AttachedFunction(ChatBot chatBot, bool allowPersistent)
    {
        return [AlifeContentUtility.BuildXmlFunction("LoadVideo", VideoFileToContentAsync, chatBot, true)];
    }

    static VideoUrlContent VideoFileToContentAsync(string file)
    {
        byte[] data = File.ReadAllBytes(file);
        return new VideoUrlContent($"data:{GetMimeType(file)};base64,{Convert.ToBase64String(data)}");

        static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }
}