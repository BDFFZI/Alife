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
[Description("使用者将获取被动接受系统事件，如开始、结束、定时器事件，并可选的控制这些信息的收发。")]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    [XmlHandler]
    [Description("使用者可以暂停系统定时器一段时间(但注意可别睡过头了)。")]
    public void PauseTimer(XmlTagContext context, [Description("单位为秒")] int duration = 0)
    {
        if (context.Status == TagStatus.OneShot || context.Status == TagStatus.Closing)
            nextTime += duration;
    }
    [XmlHandler]
    [Description("使用者可以定一个带备注的闹钟，使其可以在之后继续执行任务，如<continue>联系下主人，看看他在干啥？</continue>(但可注意别把时间算错了)")]
    public async void Continue(XmlTagContext context, string remark, [Description("延迟的秒数，默认为0")] int delay = 0)
    {
        try
        {
            if (context.Status == TagStatus.Closing || context.Status == TagStatus.OneShot)
            {
                await Task.Delay(delay * 1000);
                chatActivity.ChatBot.Poke($"[{nameof(EventService)}]嘀嘀嘀，{nameof(chatActivity.Character.Name)}之前定的闹钟响了。"
                                          + (string.IsNullOrWhiteSpace(context.FullContent) ? "无" : "还有备注：" + context.FullContent));
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

        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]{chatActivity.Character.Name}重新恢复活动了，醒来后ta决定先...({configuration.AppendStartPrompt})");

        _ = Task.Run(Update);
        //发生对话时重新计时
        chatActivity.ChatBot.ChatSent += _ => {
            currentTime = 0;
        };
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatActivity.ChatBot.ChatAsync($"[{nameof(EventService)}]{chatActivity.Character.Name}即将陷入休眠，ta在最后时决定...({configuration.AppendDestroyPrompt})");
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
                    chatActivity.ChatBot.Poke($"[{nameof(EventService)}]嘀嘀嘀，定时器又响了，在这个时间，{chatActivity.Character.Name}决定...({configuration.AppendUpdatePrompt})");

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
