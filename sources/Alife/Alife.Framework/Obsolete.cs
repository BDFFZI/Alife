using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Framework;

[Obsolete("请改用 ChatBehaviour.OnUpdate")]
public interface ITimeIterative
{
    public void OnUpdate(ref float time);
    public float DeltaTime => 1;
}

[Obsolete("请改用 ChatBehaviour")]
public interface ISystemEvent
{
    public Task AwakeAsync(AwakeContext context);
    public Task StartAsync(Kernel kernel, ChatActivity chatActivity);
    public Task DestroyAsync();
}

[Obsolete("请改用 ChatBehaviour")]
public abstract class InteractiveModule : ChatBehaviour, ISystemEvent
{
    protected ChatHistory ChatHistory { get; private set; } = null!;
    protected override async Task OnAwake()
    {
        await AwakeAsync(new AwakeContext() {
            ChatBot = ChatBot,
            Character = Character,
            ChatActivity = ChatActivity
        });
    }
    protected override async Task OnStart()
    {
        await StartAsync(new Kernel(), ChatActivity);
    }
    protected override async Task OnDestroy()
    {
        await DestroyAsync();
    }

    [Obsolete($"请改用 {nameof(ChatBehaviour)} 作为基类")]
    public virtual new Task AwakeAsync(AwakeContext context)
    {
        ChatHistory = ChatBot.ChatHistory;
        return Task.CompletedTask;
    }
    [Obsolete($"请改用 {nameof(ChatBehaviour)} 作为基类")]
    public virtual Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        if (this is ITimeIterative interactiveModule)
        {
            updateCancellation = new CancellationTokenSource();
            StartUpdate(interactiveModule, updateCancellation.Token);
        }

        return Task.CompletedTask;
    }
    [Obsolete($"请改用 {nameof(ChatBehaviour)} 作为基类")]
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

[Obsolete($"请改用 {nameof(ChatBehaviour)} 作为基类")]
public class InteractiveModule<T> : InteractiveModule
{
    protected virtual string ChatTextFilter(string text)
    {
        return $"消息来源:[{typeof(T).Name}]\n{text}";
    }

    protected void Prompt(string prompt)
    {
        ChatHistory.AddSystemMessage($"[{typeof(T).Name}]说明:\n{prompt}");
        ChatBot.UpdateHistoryEndIndex();
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
        ChatBot.Chat(ChatTextFilter(message));
    }

    protected Task ChatAsync(string message)
    {
        return ChatBot.ChatAsync(ChatTextFilter(message));
    }

    protected Task ImplicitChatAsync(string message)
    {
        return ChatBot.ImplicitChatAsync(ChatTextFilter(message));
    }
}
