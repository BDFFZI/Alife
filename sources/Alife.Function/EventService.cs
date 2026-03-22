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
[Description("你获得了被动接受系统事件的能力，例如开始、结束、定时报点事件，但你可以使用如下指令控制这些信息的收发。")]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    [XmlHandler]
    [Description("能够暂停周期性定时报点事件一段时间(单位为秒)。")]
    public void PauseTimer(XmlTagContext context, int duration = 0)
    {
        if (context.Status == TagStatus.OneShot || context.Status == TagStatus.Closing)
            nextTime += duration;
    }

    ChatBot chatBot = null!;
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
        chatBot = chatActivity.ChatBot;
        updateCancelSource = new CancellationTokenSource();

        await chatBot.ChatAsync(string.Join("\n", "[系统事件]对话活动即将开始(用消息类指令给用户打个招呼吧)", configuration.AppendStartPrompt));

        _ = Task.Run(Update);
        //发生对话时重新计时
        chatBot.ChatSent += _ => {
            currentTime = 0;
        };
    }
    public override async Task DestroyAsync()
    {
        await updateCancelSource.CancelAsync();
        await chatBot.ChatAsync(string.Join("\n", "[系统事件]对话活动即将结束(如果有事情处理，请不要耽误太多时间，否则会让用户误以为异常，而强制关闭)", configuration.AppendDestroyPrompt));
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
                if (chatBot.IsChatting)
                    continue; //聊天时不计时

                currentTime += DeltaTime;

                if (currentTime >= nextTime)
                {
                    chatBot.Poke("[系统事件]这是周期性定时报点(你可以借此做些想做的事，但不要告诉用户有报点信息)(你也可以用pauseTimer指令暂停该事件一段时间)。");

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
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
