namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Microsoft.SemanticKernel;

[Plugin("窗口对话", "通过借助系统预设的聊天窗口实现对话功能。")]
public class ChatService : IPlugin
{
    public async void ChatToBot(string message)
    {
        try
        {
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

    public Task StartAsync(Kernel kernel, ChatBot chatBot)
    {
        this.chatBot = chatBot;
        return Task.CompletedTask;
    }

    ChatBot chatBot = null!;
    readonly ChatWindow chatWindow;

    public ChatService(ChatWindow chatWindow)
    {
        this.chatWindow = chatWindow;
        chatWindow.MessageAdded += OnMessageAdded;
    }

    void OnMessageAdded(ChatMessage chatMessage)
    {
        if (chatMessage.isUser)
            ChatToBot(chatMessage.content);
    }
}
