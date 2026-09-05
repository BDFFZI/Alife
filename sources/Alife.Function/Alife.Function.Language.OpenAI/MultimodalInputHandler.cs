using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Language.OpenAI;

internal static class MultimodalInputHandler
{
    public static XmlHandler Create(
        ChatBot chatBot,
        bool enableImage,
        bool enableVideo,
        bool enableFile)
    {
        XmlHandler handler = new("MultimodalInput") {
            Description = "将图片、视频或文件加入当前对话上下文；内容加入后会自动通知你继续处理。",
            Explanation = "LoadImage 接受本机图片路径或 http(s) 图片地址。LoadVideo 和 LoadFile 仅接受模型服务可访问的 http(s) 地址。"
        };

        if (enableImage)
            handler.Functions.Add(CreateFunction("loadimage", "将图片加入当前对话上下文。", (context, _) => {
                QueueImage(chatBot, context.Parameters["path"]);
                chatBot.Poke("图片已放入上下文，请继续处理用户的请求。");
                return Task.CompletedTask;
            }));

        if (enableVideo)
            handler.Functions.Add(CreateFunction("loadvideo", "将视频 URL 加入当前对话上下文。", (context, _) => {
                QueueRemoteContent(chatBot, context.Parameters["url"], true);
                chatBot.Poke("视频已放入上下文，请继续处理用户的请求。");
                return Task.CompletedTask;
            }));

        if (enableFile)
            handler.Functions.Add(CreateFunction("loadfile", "将文件 URL 加入当前对话上下文。", (context, _) => {
                QueueRemoteContent(chatBot, context.Parameters["url"], false);
                chatBot.Poke("文件已放入上下文，请继续处理用户的请求。");
                return Task.CompletedTask;
            }));

        return handler;
    }

    static XmlFunction CreateFunction(
        string name,
        string description,
        Func<XmlContext, CancellationToken, Task> invoker)
    {
        string parameterName = name == "loadimage" ? "path" : "url";
        return new XmlFunction {
            Name = name,
            Description = description,
            Mode = FunctionMode.OneShot,
            Parameters = [new XmlParameter {
                Name = parameterName,
                Type = "String",
                Description = parameterName == "path" ? "图片本机路径或 http(s) 地址" : "模型服务可访问的 http(s) 地址"
            }],
            Invoker = invoker
        };
    }

    static void QueueImage(ChatBot chatBot, string pathOrUrl)
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

        QueueContent(chatBot, image, "将图片加入对话上下文");
    }

    static void QueueRemoteContent(ChatBot chatBot, string url, bool isVideo)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("视频和文件必须提供模型服务可访问的 http(s) 地址。", nameof(url));

        KernelContent content = isVideo ? new GlmVideoUrlContent(uri) : new GlmFileUrlContent(uri);
        QueueContent(chatBot, content, isVideo ? "将视频加入对话上下文" : "将文件加入对话上下文");
    }

    static void QueueContent(ChatBot chatBot, KernelContent content, string reason)
    {
        chatBot.QueueChatHistoryEdit(thread => thread.ChatHistory.AddUserMessage([content]), reason);
    }

    static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}
