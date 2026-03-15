namespace Alife.OfficialPlugins;

using Abstractions;
using Microsoft.SemanticKernel;

[Plugin("窗口对话", "通过借助系统预设的聊天窗口实现对话功能。")]
public class ChatService : Plugin
{
    ChatBot chatBot = null!;
    readonly ChatWindow chatWindow;

    public ChatService(ChatWindow chatWindow)
    {
        this.chatWindow = chatWindow;
        chatWindow.MessageAdded += OnMessageAdded;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        return Task.CompletedTask;
    }

    void OnMessageAdded(ChatMessage chatMessage)
    {
        if (chatMessage.isUser)
            ChatToBot(chatMessage.content);
    }

    async void ChatToBot(string? message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            ChatMessage assistantMessage = new() {
                content = "", isUser = false, isInputting = true
            };
            chatWindow.AddMessage(assistantMessage);

            await foreach (string content in chatBot.ChatStreamingAsync(message))
            {
                assistantMessage.content += content;
                chatWindow.UpdateMessage(assistantMessage);
            }

            assistantMessage.isInputting = false;
            chatWindow.UpdateMessage(assistantMessage);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
