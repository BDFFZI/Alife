using System.ComponentModel;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.OfficialPlugins;
using Microsoft.SemanticKernel;

public class EventServiceData
{
    public string? AppendStartPrompt { get; set; }
    public string? AppendDestroyPrompt { get; set; }
    public string? AppendUpdatePrompt { get; set; }
    public int UpdateInterval { get; set; } = 120;
    public int UpdateRandomOffset { get; set; } = 60;
}

[Plugin("系统事件", "让AI可以获取到各种系统事件的提醒。", LaunchOrder = 100)]
[Description("你能够接收到系统事件（如开始、结束、周期报点），并可选的控制这些信息的收发。")]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    [XmlFunction]
    [Description("暂停系统周期报点一段时间（注意确保暂停期间你没有其他事务）。")]
    public void PauseTimer(XmlExecutorContext context, [Description("暂停持续时间，单位为秒")] int duration = 0)
    {
        if (context.CallMode == CallMode.OneShot || context.CallMode == CallMode.Closing)
            nextTime += duration;
    }
    [XmlFunction]
    [Description("定一个可带备注的延迟提醒，如<continue delay=\"60\">联系下主人，看看他在干啥？</continue>（注意不要算错时间差）")]
    public async void Continue(XmlExecutorContext context, string remark, [Description("延迟的秒数，默认为0")] int delay = 0)
    {
        try
        {
            if (context.CallMode == CallMode.Closing || context.CallMode == CallMode.OneShot)
            {
                await Task.Delay(delay * 1000);
                chatActivity.ChatBot.Poke($"[{nameof(EventService)}]来自continue的延迟提醒，你可以进行你的行动了。"
                                          + (string.IsNullOrWhiteSpace(context.FullContent) ? "" : "\n备注：" + context.FullContent));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    ChatActivity chatActivity = null!;
    EventServiceData configuration = null!;
    CancellationTokenSource updateCancelSource = null!;
    const int DeltaTime = 1;
    int currentTime;
    int nextTime;

    public EventService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }
    public void Configure(EventServiceData configuration)
    {
        this.configuration = configuration;
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        this.chatActivity = chatActivity;
        updateCancelSource = new CancellationTokenSource();

        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]对话活动重新开始，请按要求检查记忆文件。({configuration.AppendStartPrompt})");

        _ = Task.Run(Update);
        //发生对话时重新计时
        chatActivity.ChatBot.ChatSent += _ => {
            currentTime = 0;
        };
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]对话活动即将关闭，请按要求保存记忆文件。({configuration.AppendDestroyPrompt})");
    }
    async void Update()
    {
        try
        {
            PeriodicTimer updateTimer = new(TimeSpan.FromSeconds(DeltaTime));
            updateCancelSource = new CancellationTokenSource();

            //进入定时循环
            nextTime = NextTime();
            while (await updateTimer.WaitForNextTickAsync(updateCancelSource.Token))
            {
                if (chatActivity.ChatBot.IsChatting)
                    continue; //聊天时不计时

                currentTime += DeltaTime;

                if (currentTime >= nextTime)
                {
                    chatActivity.ChatBot.Poke($"[{nameof(EventService)}]系统周期报点，请注意当前时间，你可以乘机整理记忆，或尝试与用户打招呼（注意选择合适的联系方式和话题）({configuration.AppendUpdatePrompt})");

                    currentTime = 0;
                    nextTime = NextTime();
                }
            }

            int NextTime()
            {
                Random random = new Random();
                int offset = random.Next(-configuration.UpdateRandomOffset, configuration.UpdateRandomOffset);
                return configuration.UpdateInterval + offset;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
