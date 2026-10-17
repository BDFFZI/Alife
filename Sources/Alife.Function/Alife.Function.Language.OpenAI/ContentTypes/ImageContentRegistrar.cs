using System;
using System.IO;
using System.Text.Json.Nodes;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>图片内容：协议序列化为 <c>image_url</c>。AI 通过 <c>LoadImage</c> 查看，用 temp 参数选择临时/保留。</summary>
public sealed class ImageContentRegistrar : AlifeContentRegistrar
{
    public override string DisplayName => "图片输入";
    public override string ProtocolTypeName => "image_url";
    public override Type ContentType => typeof(ImageContent);

    public override JsonObject SerializeContent(KernelContent content)
    {
        ImageContent image = (ImageContent)content;
        return new JsonObject {
            ["type"] = "image_url",
            ["image_url"] = new JsonObject {
                ["url"] = GetImageUrl(image)
            }
        };

        static string GetImageUrl(ImageContent image)
        {
            if (image.Uri is not null)
                return image.Uri.ToString();
            if (image.DataUri is not null)
                return image.DataUri;
            if (image.Data is { IsEmpty: false } data)
                return $"data:{image.MimeType ?? "application/octet-stream"};base64,{Convert.ToBase64String(data.Span)}";
            throw new NotSupportedException("ImageContent 必须包含 URL、data URI 或二进制数据。");
        }
    }

    public override XmlFunction[] AttachedFunction(ChatBot chatBot, bool allowPersistent)
    {
        return [AlifeContentUtility.BuildXmlFunction("LoadImage", ImageFileToContentAsync, chatBot, true)];
    }

    static KernelContent ImageFileToContentAsync(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        return new ImageContent(bytes, GetMimeType(file, bytes));

        //无扩展名 URL（如 QQ 下载链）下载后是无后缀临时文件，只按扩展名会回退 octet-stream 被上游拒收，
        //所以优先按文件头嗅探，扩展名仅作补充。
        static string GetMimeType(string path, byte[] data)
        {
            string? sniffed = SniffImageMime(data);
            if (sniffed != null)
                return sniffed;

            string fromExtension = Path.GetExtension(path).ToLowerInvariant() switch {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => ""
            };
            if (fromExtension != "")
                return fromExtension;

            //下载到的大概率是鉴权过期返回的 HTML/JSON 而非图片，直接抛错比发 octet-stream 更易排查
            if (IsTextResponse(data))
                throw new NotSupportedException("下载的内容不是图片（可能是链接过期返回了文本），请更换链接重试。");

            return "image/jpeg";
        }

        static string? SniffImageMime(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";
            if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "image/png";
            if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
                return "image/gif";
            if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                return "image/webp";
            if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
                return "image/bmp";
            return null;
        }

        static bool IsTextResponse(byte[] data)
        {
            int i = 0;
            while (i < data.Length && (data[i] == 0x20 || data[i] == 0x09 || data[i] == 0x0D || data[i] == 0x0A || data[i] == 0xEF ||
                                       data[i] == 0xBB || data[i] == 0xBF))
                i++;
            if (i >= data.Length)
                return true;
            byte b = data[i];
            return b == 0x3C || b == 0x7B || b == 0x5B; // < / { / [
        }
    }
}