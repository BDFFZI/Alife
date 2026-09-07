using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>文件内容：协议序列化为 <c>file</c>；AI 可通过 <c>loadfile</c> 上传 URL。</summary>
public sealed class FileUrlContent(Uri url) : KernelContent
{
    public Uri Url { get; } = url;
}

public sealed class FileUrlContentType : AlifeContentHandlerBase
{
    public override Type ContentType => typeof(FileUrlContent);
    public override string DisplayName => "文件输入";
    public override string ProtocolTypeName => "file";

    public override JsonObject SerializeContent(KernelContent content)
    {
        FileUrlContent file = (FileUrlContent)content;
        return new JsonObject { ["type"] = "file", ["file"] = new JsonObject { ["file_url"] = file.Url.ToString() } };
    }

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config)
    {
        if (IsEnabled(config) == false)
            return null;
        return BuildXmlFunction(
            "loadfile",
            "一般仅支持部分文件类型，如PDF、Excel、TXT等常见的办公文本类文件",
            "url",
            "可直链访问的网络地址",
            async (context, _) => {
                await LoadFileAsync(chatBot, context.Parameters["url"]);
                chatBot.Poke(ContentType.Name + "内容");
            });
    }

    static async Task LoadFileAsync(ChatBot chatBot, string url)
    {
        Uri uri = RequireHttpUrl(url, nameof(url));
        await QueueContentAsync(chatBot, new FileUrlContent(uri), "将文件加入对话上下文");
    }
}