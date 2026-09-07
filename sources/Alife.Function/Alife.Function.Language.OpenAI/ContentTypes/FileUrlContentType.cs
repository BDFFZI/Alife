using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;

namespace Alife.Function.Language.OpenAI;

/// <summary>文件内容：协议序列化为 <c>file</c>。AI 通过 <c>LookFile</c> 查看，用 mode 参数选择临时/保留。</summary>
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

    public override XmlFunction? CreateXmlFunction(ChatBot chatBot, OpenAILanguageModelConfig config, IMultimodalExecutor executor)
    {
        if (IsEnabled(config) == false)
            return null;

        return BuildXmlFunction(
            "LookFile",
            "一般仅支持部分文件类型，如PDF等常见文本文件。",
            [
                ("url", "可直链访问的网络地址", "String"),
                ("keep", "是否常驻上下文以便连续分析，默认 true", "bool"),
            ],
            async (context, ct) => {
                FileUrlContent file = new(RequireHttpUrl(context.Parameters["url"], "url"));
                bool persistent = IsPersistentRequested(context);
                if (persistent && executor.IsPersistentAllowed(RegistrationKey!) == false)
                {
                    chatBot.Poke("保留模式未授权，仅可使用 keep=false 临时查看。");
                    return;
                }
                if (persistent)
                {
                    await QueueContentAsync(chatBot, file, "将文件加入对话上下文");
                    chatBot.Poke("已上传");
                }
                else
                {
                    try
                    {
                        string result = await executor.CompleteWithContentAsync(chatBot, file, ct);
                        chatBot.Poke(string.IsNullOrWhiteSpace(result) ? "未能获取文件内容。" : result);
                    }
                    catch (Exception e)
                    {
                        chatBot.Poke($"文件查看失败：{e.Message}");
                    }
                }
            });
    }
}