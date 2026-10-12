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
        return new VideoUrlContent($"data:{GetMimeType(file, data)};base64,{Convert.ToBase64String(data)}");

        static string GetMimeType(string path, byte[] data)
        {
            string? sniffed = SniffVideoMime(data);
            if (sniffed != null)
                return sniffed;

            string fromExtension = Path.GetExtension(path).ToLowerInvariant() switch {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mkv" => "video/x-matroska",
                ".webm" => "video/webm",
                _ => ""
            };
            if (fromExtension != "")
                return fromExtension;

            return "video/mp4";
        }

        static string? SniffVideoMime(byte[] data)
        {
            //MP4/MOV/M4V: ....ftyp
            if (data.Length >= 12 && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70)
            {
                //ftypqt 为 MOV，其余按 MP4 处理
                if (data.Length >= 12 && data[8] == 0x71 && data[9] == 0x74)
                    return "video/quicktime";
                return "video/mp4";
            }
            //AVI/WAV: RIFF....AVI / RIFF....WAVE
            if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
            {
                if (data[8] == 0x41 && data[9] == 0x56 && data[10] == 0x49)
                    return "video/x-msvideo";
                if (data.Length >= 16 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                    return "video/webm";
            }
            //MKV/WEBM: 1A 45 DF A3
            if (data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
                return "video/webm";
            return null;
        }
    }
}