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
                AlifeLog.LogInformation($"{GetType().Name}::OnDestroy");

                //OnDestroy 是插件代码，可能因为等待网络、模型或锁而卡住。
                //加超时保护：单个模块的销毁不能拖死整个角色停止/应用退出。
                TimeSpan timeout = DestroyTimeout;
                Task destroy = OnDestroy();
                Task finished = await Task.WhenAny(destroy, Task.Delay(timeout));
                if (finished == destroy)
                {
                    await destroy;   //让异常继续被下面的 catch 捕获
                }
                else
                {
                    //关键：只"不再等待"是不够的 —— 模型调用还在后台空转、继续思考。
                    //主动打断 ChatBot 的当前对话，让 ChatStreamingAsync 抛取消异常真正收尾。
                    //这是"道别思考闭环"在框架层唯一能做的兜底：限时 + 真取消。
                    if (ChatBot != null)
                    {
                        try { ChatBot.ChatBreakTokenSource.Cancel(); }
                        catch { }
                    }

                    AlifeLog.LogWarning(
                        $"{GetType().Name}::OnDestroy 超过 {timeout.TotalSeconds:0.#}s 未返回，" +
                        "已打断在途对话并跳过等待");
                }
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }

    /// <summary>
    /// OnDestroy 的宽限期。普通模块 8s 足够；
    /// 需要在销毁时生成道别的模块可重写为更长（例如 30s），给模型足够的思考时间。
    /// 超时后会主动打断 ChatBot 的在途对话，不会让模型在后台空转。
    /// </summary>
    protected virtual TimeSpan DestroyTimeout => TimeSpan.FromSeconds(8);

    /// <summary>供容器读取实际宽限期，用于给它留足余量（框架内部使用）。</summary>
    internal TimeSpan EffectiveDestroyTimeout => DestroyTimeout;

    protected virtual Task OnAwake() => Task.CompletedTask;
    protected virtual Task OnStart() => Task.CompletedTask;
    protected virtual Task OnUpdate() => Task.CompletedTask;
    protected virtual Task OnDestroy() => Task.CompletedTask;

    readonly CancellationTokenSource destroyCancellationTokenSource = new();
}