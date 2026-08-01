using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Framework;

public struct AwakeContext
{
    public Character Character { get; init; }
    public ChatBot ChatBot { get; init; }
    public ChatActivity ChatActivity { get; init; }
}

public struct UpdateContext
{
    public int FrameCount { get; init; }
    public float RealTime { get; init; }
    public float ExpectedDeltaTime { get; init; }
    public float DeltaTime { get; init; }
    public float Time { get; init; }
}

public class ChatBehaviour : IAsyncDisposable
{
    public bool IsAwaked { get; private set; }
    public bool IsStarted { get; private set; }
    public Character Character { get; private set; } = null!;
    public ChatBot ChatBot { get; private set; } = null!;
    public ChatActivity ChatActivity { get; private set; } = null!;
    public UpdateContext UpdateContext { get; private set; }
    public CancellationToken DestroyCancellationToken => destroyCancellationTokenSource.Token;

    public async Task AwakeAsync(AwakeContext context)
    {
        Character = context.Character;
        ChatBot = context.ChatBot;
        ChatActivity = context.ChatActivity;

        try
        {
            await OnAwake();
            IsAwaked = true;
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
    public async Task UpdateAsync(UpdateContext context)
    {
        if (IsAwaked == false)
            return;

        UpdateContext = context;
        
        if (!IsStarted)
        {
            try
            {
                await OnStart();
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }

            IsStarted = true;
            return;
        }

        try
        {
            await OnUpdate();
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await destroyCancellationTokenSource.CancelAsync();

        if (IsAwaked)
        {
            try
            {
                await OnDestroy();
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }

    protected virtual Task OnAwake() => Task.CompletedTask;
    protected virtual Task OnStart() => Task.CompletedTask;
    protected virtual Task OnUpdate() => Task.CompletedTask;
    protected virtual Task OnDestroy() => Task.CompletedTask;

    CancellationTokenSource destroyCancellationTokenSource = new();
}
