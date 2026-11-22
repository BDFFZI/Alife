using System;
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
            AlifeLog.LogInformation($"{GetType().Name}::OnAwake");
            await OnAwake();
            IsAwaked = true;
        }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
    public async Task StartOrUpdateAsync(UpdateContext context)
    {
        if (IsAwaked == false)
            return;

        UpdateContext = context;

        if (!IsStarted)
        {
            try
            {
                AlifeLog.LogInformation($"{GetType().Name}::OnStart");
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
    public async Task EndAsync()
    {
        if (IsStarted)
        {
            try
            {
                AlifeLog.LogInformation($"{GetType().Name}::OnEnd");
                await OnEnd();
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
    public virtual async ValueTask DisposeAsync()
    {
        await destroyCancellationTokenSource.CancelAsync();

        if (IsAwaked)
        {
            try
            {
                AlifeLog.LogInformation($"{GetType().Name}::OnDestroy");
                await OnDestroy();
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }

    /// <summary>
    /// 插件已全部实例化，但未初始化，功能不一定可用
    /// </summary>
    /// <returns></returns>
    protected virtual Task OnAwake() => Task.CompletedTask;
    /// <summary>
    /// 插件已完全初始化，可以自由使用
    /// </summary>
    /// <returns></returns>
    protected virtual Task OnStart() => Task.CompletedTask;
    /// <summary>
    /// 默认的每帧时间，可用于简化定时任务的实现
    /// </summary>
    /// <returns></returns>
    protected virtual Task OnUpdate() => Task.CompletedTask;
    /// <summary>
    /// 角色活动即将停止，但各插件还处于可正常工作的阶段
    /// </summary>
    /// <returns></returns>
    protected virtual Task OnEnd() => Task.CompletedTask;
    /// <summary>
    /// 插件陆续销毁，功能不一定可用
    /// </summary>
    /// <returns></returns>
    protected virtual Task OnDestroy() => Task.CompletedTask;

    readonly CancellationTokenSource destroyCancellationTokenSource = new();
}