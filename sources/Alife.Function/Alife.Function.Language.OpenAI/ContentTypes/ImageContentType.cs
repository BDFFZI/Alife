using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>图片内容：协议序列化为 <c>image_url</c>；AI 可通过 <c>loadimage</c> 上传。</summary>
public sealed class ImageContentType : AlifeContentHandlerBase
{
    public override Type ContentType => typeof(ImageContent);
    public override string DisplayName => "图片输入";
    public override string ProtocolTypeName => "image_url";

    public override JsonObject SerializeContent(KernelContent content)
    {
        ImageContent image = (ImageContent)content;
        return new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = GetImageUrl(image) } };
    }

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config)
    {
        if (IsEnabled(config) == false)
            return null;
        return BuildXmlFunction(
            "loadimage",
            null,
            "path",
            "图片本机路径或可直链访问的Url地址",
            async (context, _) => {
                await LoadImageAsync(chatBot, context.Parameters["path"]);
                chatBot.Poke(ContentType.Name + "内容");
            });
    }

    static async Task LoadImageAsync(ChatBot chatBot, string pathOrUrl)
    {
        ImageContent image;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            image = new ImageContent(uri);
        }
        else
        {
            if (File.Exists(pathOrUrl) == false)
                throw new FileNotFoundException("图片不存在", pathOrUrl);

            image = new ImageContent(File.ReadAllBytes(pathOrUrl), GetMimeType(pathOrUrl));
        }

        await QueueContentAsync(chatBot, image, "将图片加入对话上下文");
    }

    static string GetImageUrl(ImageContent image)
    {
        if (image.Uri is not null)
            return image.Uri.ToString();
        if (image.DataUri is not null)
            return image.DataUri;
        if (image.Data is { } data && data.IsEmpty == false)
            return $"data:{image.MimeType ?? "application/octet-stream"};base64,{Convert.ToBase64String(data.Span)}";
        throw new NotSupportedException("ImageContent 必须包含 URL、data URI 或二进制数据。");
    }

    static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}