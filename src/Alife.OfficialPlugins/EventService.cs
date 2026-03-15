using System.ComponentModel;

namespace Alife.OfficialPlugins;

using Abstractions;
using Microsoft.SemanticKernel;

public class EventServiceData
{
    public string? AppendStartPrompt { get; set; }
    public string? AppendDestroyPrompt { get; set; }
    public string? AppendUpdatePrompt { get; set; }
    public int UpdateInterval { get; set; } = 600;
    public int UpdateRandomOffset { get; set; } = 120;
}
[Plugin("系统事件", "让AI可以获取到系统事件的提醒。", LaunchOrder = 100)]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    public override Task AwakeAsync(AwakeContext context)
    {
        //增加“继续”功能，让AI能够进行连续性的工作流
        context.kernelBuilder.Plugins.AddFromObject(this);
        return Task.CompletedTask;
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        updateCancelSource = new CancellationTokenSource();

        await chatBot.ChatAsync(string.Join("\n", "[触发会话开始](这不是用户内容，非必要不要回复！)", configuration.AppendStartPrompt));
        _ = Task.Run(Update);
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatBot.ChatAsync(string.Join("\n", "[触发会话结束](这不是用户内容，非必要不要回复！如果有事情处理，请不要耽误太多时间，否则会让用户误以为异常，而强制关闭)", configuration.AppendDestroyPrompt));
    }
    async void Update()
    {
        try
        {
            int currentTime = 0;
            int nextTime = NextTime();

            const int DeltaTime = 10;
            PeriodicTimer updateTimer = new(TimeSpan.FromSeconds(DeltaTime));
            updateCancelSource = new CancellationTokenSource();

            //进入定时循环
            while (await updateTimer.WaitForNextTickAsync(updateCancelSource.Token))
            {
                if (chatBot.IsChatting)
                    continue; //聊天时不计时

                currentTime += DeltaTime;

                if (currentTime >= nextTime)
                {
                    chatBot.Poke("[触发周期定时]默认情况下请直接回复空文本，这不是用户消息。");

                    currentTime = 0;
                    nextTime = NextTime();
                }
            }

            //发生对话时重新计时
            chatBot.ChatStart += () => {
                currentTime = 0;
            };

            int NextTime()
            {
                Random random = new Random();
                int offset = random.Next(-configuration.UpdateRandomOffset, configuration.UpdateRandomOffset);
                return configuration.UpdateInterval + offset;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    [KernelFunction]
    [Description("当你需要主动连续对话时，可以使用该函数。调用后会自动回传，让你有机会继续讲话。")]
    public void Continue() { }

    ChatBot chatBot = null!;
    EventServiceData configuration = null!;
    CancellationTokenSource updateCancelSource = null!;

    public void Configure(EventServiceData configuration)
    {
        this.configuration = configuration;
    }
}
