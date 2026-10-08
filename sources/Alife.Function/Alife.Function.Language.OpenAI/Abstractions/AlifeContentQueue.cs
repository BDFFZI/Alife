using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Language.OpenAI;

public static class AlifeContentQueue
{
    public static void Add(KernelContent content, bool isTemp)
    {
        Contents.Add((content, isTemp));
    }
    public static async void Flash(ChatBot chatBot)
    {
        try
        {
            //清理临时多模态资源
            if (TempContents.Count != 0)
            {
                await chatBot.EditChatHistoryAsync(_ => {
                    foreach ((ChatMessageContent, KernelContent) tempContent in TempContents)
                        tempContent.Item1.Items.Remove(tempContent.Item2);
                    return Task.CompletedTask;
                }, "检测移除临时内容");

                TempContents.Clear();
            }

            //发送多模态内容
            if (Contents.Count != 0)
            {
                ChatMessageContent chatMessageContent = new(AuthorRole.User, "");
                foreach ((KernelContent kernelContent, bool isTemp) in Contents)
                {
                    chatMessageContent.Items.Add(kernelContent);
                    chatMessageContent.Content += isTemp ? "[多模态内容(临时)]" : "[多模态内容]";
                    if (isTemp)
                        TempContents.Add((chatMessageContent, kernelContent));
                }
                Contents.Clear();

                _ = chatBot.ChatAsync(chatMessageContent, false).ContinueWith(async task => {
                    ChatResult result = task.Result;

                    if (result.Exception != null)
                    {
                        await chatBot.EditChatHistoryAsync(thread => {
                            thread.ChatHistory.Remove(chatMessageContent);
                            return Task.CompletedTask;
                        }, "移除多模态资源");
                    }

                    if (result.Exception != null)
                        chatBot.Poke("多模态内容加载失败：" + result.Exception.Message);
                });
            }
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }

    static readonly List<(KernelContent, bool)> Contents = new();
    static readonly List<(ChatMessageContent, KernelContent)> TempContents = new();
}