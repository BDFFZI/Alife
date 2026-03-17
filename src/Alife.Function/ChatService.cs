namespace Alife.OfficialPlugins;

using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Alife.Interpreter;

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

    ChatMessage? assistantMessage;

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        // 当 AI 开始回复时（或者发送任何消息给 AI 时），准备显示
        chatBot.ChatSent += (m) => {
            if (chatBot.IsChatting) {
                assistantMessage = new() { content = "", isUser = false, isInputting = true };
                chatWindow.AddMessage(assistantMessage);
            }
        };
        // 接收到流式内容
        chatBot.ChatReceived += (content) => {
            if (assistantMessage != null)
            {
                assistantMessage.content += content;
                chatWindow.UpdateMessage(assistantMessage);
            }
        };
        // 会话结束
        chatBot.ChatOver += () => {
            if (assistantMessage != null)
            {
                assistantMessage.isInputting = false;
                chatWindow.UpdateMessage(assistantMessage);
                assistantMessage = null;
            }
        };
        return Task.CompletedTask;
    }

    void OnMessageAdded(ChatMessage chatMessage)
    {
        if (chatMessage.isUser)
            chatBot.Chat(chatMessage.content);
    }
}
