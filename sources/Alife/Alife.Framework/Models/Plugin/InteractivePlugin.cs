using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

public abstract class InteractivePlugin : ISystemEvent
{
    protected Character Character { get; private set; } = null!;
    protected ChatActivity ChatActivity { get; private set; } = null!;
    protected ChatBot ChatBot { get; private set; } = null!;
    protected ChatHistory ChatHistory { get; private set; } = null!;

    public virtual Task AwakeAsync(AwakeContext context)
    {
        Character = context.Character;
        ChatHistory = context.ContextBuilder.ChatHistory;

        return Task.CompletedTask;
    }
    public virtual Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        ChatActivity = chatActivity;
        ChatBot = chatActivity.ChatBot;

        if (this is ITimeIterative interactivePlugin)
        {
            updateCancellation = new CancellationTokenSource();
            StartUpdate(interactivePlugin, updateCancellation.Token);
        }

        return Task.CompletedTask;
    }
    public virtual Task DestroyAsync()
    {
        if (updateCancellation != null)
            return updateCancellation.CancelAsync();

        return Task.CompletedTask;
    }

    CancellationTokenSource? updateCancellation;

    static async void StartUpdate(ITimeIterative handler, CancellationToken token)
    {
        try
        {
            DateTime startTime = DateTime.Now;
            while (!token.IsCancellationRequested)
            {
                await Task.Delay((int)(handler.DeltaTime * 1000), token);
                float seconds = (float)(DateTime.Now - startTime).TotalSeconds;
                handler.OnUpdate(ref seconds);
                startTime = DateTime.Now - TimeSpan.FromSeconds(seconds);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

public class InteractivePlugin<T> : InteractivePlugin
{
    protected virtual string ChatTextFilter(string text)
    {
        return $"[来自{typeof(T).Name}的消息]{text}";
    }

    protected void Prompt(string prompt)
    {
        ChatHistory.AddSystemMessage($"# [{typeof(T).Name}]说明文档：\n{prompt}");
    }

    protected void Throw(string error)
    {
        throw new Exception($"[{typeof(T).Name}] 发生错误\n{error}");
    }

    protected void Poke(string message)
    {
        ChatBot.Poke(ChatTextFilter(message));
    }

    protected void Chat(string message)
    {
        ChatBot.Chat(ChatTextFilter(message), visibility: ChatInputVisibility.Internal);
    }

    protected Task ChatAsync(string message)
    {
        return ChatBot.ChatAsync(ChatTextFilter(message), visibility: ChatInputVisibility.Internal);
    }

    protected void ChatVisible(string message)
    {
        ChatBot.Chat(ChatTextFilter(message), visibility: ChatInputVisibility.Visible);
    }

    protected Task ChatAsyncVisible(string message)
    {
        return ChatBot.ChatAsync(ChatTextFilter(message), visibility: ChatInputVisibility.Visible);
    }

    protected Task ImplicitChatAsync(string message)
    {
        return ChatBot.ImplicitChatAsync(ChatTextFilter(message));
    }
}
