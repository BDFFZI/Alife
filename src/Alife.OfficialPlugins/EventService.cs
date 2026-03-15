namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

[Plugin("系统事件", "让AI可以获取到系统事件的提醒。")]
public class EventService : IPlugin
{
    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread context) => Task.CompletedTask;
    public Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        return chatBot.ChatAsync("[系统事件] 对话活动即将开始。你可以尝试读取记忆（如果有相关功能的话），准备开场词等。");
    }
    public Task DestroyAsync()
    {
        return chatBot.ChatAsync("[系统事件] 对话活动即将结束。你可以尝试保存记忆（如果有相关功能的话），道别等。");
    }

    ChatBot chatBot = null!;
}
