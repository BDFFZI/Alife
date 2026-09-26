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
        return new ImageContent(File.ReadAllBytes(file), GetMimeType(file));

        static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}