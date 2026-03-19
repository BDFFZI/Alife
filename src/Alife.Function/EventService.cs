using Alife.Abstractions;
using Microsoft.SemanticKernel;

public class EventServiceData
{
    public string? AppendStartPrompt { get; set; }
    public string? AppendDestroyPrompt { get; set; }
    public string? AppendUpdatePrompt { get; set; }
    public int UpdateInterval { get; set; } = 120;
    public int UpdateRandomOffset { get; set; } = 60;
}
[Plugin("系统事件", "让AI可以获取到系统事件的提醒。", LaunchOrder = 100)]
public class EventService : Plugin, IConfigurable<EventServiceData>
{
    ChatBot chatBot = null!;
    EventServiceData configuration = null!;
    CancellationTokenSource updateCancelSource = null!;

    public void Configure(EventServiceData configuration)
    {
        this.configuration = configuration;
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        updateCancelSource = new CancellationTokenSource();

        await chatBot.ChatAsync(string.Join("\n", "[系统事件]对话活动即将开始", configuration.AppendStartPrompt));
        _ = Task.Run(Update);
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
                    chatBot.Poke("[系统事件]这是周期性定时报点。");

                    currentTime = 0;
                    nextTime = NextTime();
                }
            }

            //发生对话时重新计时
            chatBot.ChatSent += _ => {
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
}
