using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Language.OpenAI;

public static class AlifeContentUtility
{
    public static XmlFunction BuildXmlFunction(
        string name,
        Func<string, KernelContent> fileToContent,
        ChatBot chatBot, bool allowPersistent)
    {
        return BuildXmlFunction(
            name,
            null,
            [
                ("pathOrUrl", "", "String"),
                ("persistent", "将内容常驻上下文以持续分析", "bool"),
            ],
            async (context, cancellationToken) => {
                bool persistent = IsPersistentRequested(context);
                if (persistent && allowPersistent == false)
                    throw new Exception("persistent模式未授权，无法使用。");

                string pathOrUrl = context.Parameters["pathOrUrl"];
                bool isUrl = IsUrl(pathOrUrl);
                string path = isUrl ? await UrlToPath(pathOrUrl) : pathOrUrl;
                KernelContent content = fileToContent(path);
                if (isUrl) //url被转换为了本地临时路径，故删除
                    File.Delete(path);

                ChatMessageContent chatMessageContent = new(AuthorRole.User, [content]) {
                    Content = $"[多模态内容({content.GetType().Name})]"
                };
                _ = chatBot.ChatAsync(chatMessageContent, false).ContinueWith(async task => {
                    ChatResult result = task.Result;

                    if (result.Exception != null || persistent == false)
                    {
                        await chatBot.EditChatHistoryAsync(thread => {
                            thread.ChatHistory.Remove(chatMessageContent);
                            return Task.CompletedTask;
                        }, "移除多模态资源");
                    }

                    if (result.Exception != null)
                        chatBot.Poke("多模态内容加载失败：" + result.Exception.Message);
                }, cancellationToken);
            });

        static bool IsPersistentRequested(XmlContext context)
        {
            return context.Parameters.TryGetValue("persistent", out string? persistent) && (
                persistent.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                persistent.Equals("1", StringComparison.OrdinalIgnoreCase));
        }
    }

    public static XmlFunction BuildXmlFunction(
        string name,
        string? description,
        IEnumerable<(string Name, string Description, string Type)> parameters,
        Func<XmlContext, CancellationToken, Task> invoker)
    {
        return new XmlFunction {
            Name = name,
            Description = description,
            Mode = FunctionMode.OneShot,
            Parameters = parameters
                .Select(p => new XmlParameter { Name = p.Name, Type = p.Type, Description = p.Description })
                .ToList(),
            Invoker = invoker
        };
    }

    /// <summary>
    /// 把"路径或地址"归一化为可直接读取的本地文件路径。
    /// 若 URL 无扩展名，通过 HEAD 请求获取 Content-Type 补齐。
    /// </summary>
    public static async Task<string> UrlToPath(string pathOrUrl)
    {
        string extension = Path.GetExtension(pathOrUrl.Split('?')[0]);
        if (string.IsNullOrEmpty(extension))
            extension = await GetExtensionFromContentTypeAsync(pathOrUrl);

        string tempPath = Path.Combine(
            AlifePath.TempFolderPath,
            $"{Guid.NewGuid():N}{extension}");

        await AlifeUtility.DownloadFileAsync(pathOrUrl, tempPath, timeout: TimeSpan.FromMinutes(3));
        return tempPath;

        static async Task<string> GetExtensionFromContentTypeAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                if (url.Contains("multimedia.nt.qq.com.cn") || url.Contains("qpic.cn"))
                    request.Headers.Add("Referer", "https://q.qq.com/");

                using var response = await HttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string? contentType = response.Content.Headers.ContentType?.ToString();
                    if (!string.IsNullOrEmpty(contentType))
                    {
                        return ContentTypeToExtension(contentType);
                    }
                }
            }
            catch
            {
                // 静默失败，回退到空扩展名
            }
            return string.Empty;
        }

        static string ContentTypeToExtension(string contentType)
        {
            return contentType.Split(';')[0].Trim() switch {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/bmp" => ".bmp",
                "image/svg+xml" => ".svg",
                "image/tiff" => ".tiff",
                "audio/mpeg" => ".mp3",
                "audio/wav" => ".wav",
                "audio/ogg" => ".ogg",
                "audio/m4a" => ".m4a",
                "audio/aac" => ".aac",
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "video/quicktime" => ".mov",
                "video/x-msvideo" => ".avi",
                _ => string.Empty,
            };
        }
    }
    public static bool IsUrl(string pathOrUrl)
    {
        return pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    static readonly HttpClient HttpClient = new() {
        Timeout = TimeSpan.FromSeconds(10)
    };
}