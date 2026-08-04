using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.SystemEvent;

public class SystemEventServiceConfig
{
    public string? StartPrompt { get; set; } = "(所有系统状态，如功能开关、当前位置，定时器等已全部重置)";
    public string? DestroyPrompt { get; set; } = "(系统已逐步关闭，不要执行强功能指令，仅可尝试道别操作)";

    public string? UpdatePrompt { get; set; } =
        "(如果你手头还有事情，请继续。否则你可以自由活动，比如主动找主人玩，或看新闻学知识、发起新话题、偷窥主人屏幕、去Q群找人聊天玩耍，等各种你能想象到的事)";

    public int UpdateInterval { get; set; } = 90;
    public int UpdateRandomOffset { get; set; } = 30;
    public int UpdateIntervalMultiplier { get; set; } = 3;
    public int UpdateMaxRetryCount { get; set; } = 4;
}

[Module(
    "主动事件",
    "让AI可以获取到各种系统事件的提醒。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = 100, //期望在末尾创建，以便获得栈顶事件顺序来发送启动关闭提醒
    EditorUI = typeof(SystemEventServiceUI))]
public class SystemEventService(
    XmlFunctionCaller functionService,
    IInteractor<SystemEventService> interactor) :
    ChatBehaviour,
    IConfigurable<SystemEventServiceConfig>
{
    public SystemEventServiceConfig Configuration { get; set; } = null!;

    public (DateTime Time, string Name)[] ActiveTasks => [
        (timeTask[0].Item1, "自动报点"),
        (timeTask[1].Item1, "")
    ]; //暴露给UI的数据

    [XmlFunction(FunctionMode.OneShot)]
    [Description("让自己等待几秒再继续（通常仅用于主动追问或等待外部进程，因为内部工具通常支持回调，所以不需要使用）")]
    public async Task Await(int second)
    {
        if (second > 60)
            throw new Exception($"不支持等待超过60秒，长时间等待请使用<{nameof(Awake)}>模拟");

        await Task.Delay(second * 1000);
        interactor.Poke("AWait已完成");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("创建一个定点报时，同时重置系统周期报点（使自己可以持续活跃一段时间）")]
    public void Awake([Description("格式为ISO-8601")] DateTime time, string remark = "")
    {
        ActiveTasks[1].Name = remark;
        timeTask[1] = (time, () => {
            interactor.Poke($"AWake报点：{remark}");
            timeTask[1] = (DateTime.MaxValue, () => { }); //关闭定时提醒
        });

        continuousTimerCount = 0;
        NextTimer();

        interactor.Poke($"已在 {time} 设置事件");
    }

    readonly (DateTime, Action)[] timeTask = new (DateTime, Action)[2]; //1为自动定时器，2为定时提醒
    int continuousTimerCount;

    protected override Task OnAwake()
    {
        interactor.ChatTextFilter = text => $"[系统报点]{text}";

        ChatBot.ChatSent += OnChatSent;

        XmlHandler xmlHandler = new(this) {
            Description = "当你需要主动控制你的日程，想保持活跃时，请使用该功能。",
            Explanation = """
                          主动性的表现，会让自己看着更自然更有趣更讨人喜欢。而且使得你有机会主动做一些自己的事情。因此善用报点事件，能让你变的非常强大。
                          例如偷偷记下主人的日常起居时间，来个早晚问候，或白天主动找用户聊天，这些都会让用户感到非常惊喜。
                          """
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override Task OnStart()
    {
        //设置定时
        timeTask[1].Item1 = DateTime.MaxValue;
        NextTimer();

        //发送系统启动消息
        if (ChatBot.ChatHistory.All(content => content.Role != AuthorRole.Assistant))
        {
            ChatBot.Chat("""
                         角色已激活：
                         这是你第一次苏醒，初来乍到这个陌生环境，学习利用上下文中的工具了解这个世界。
                         此外最重要的一件事，就是现在用上你丰富的能力，先向用户华丽的打个招呼吧！
                         """);
        }
        else
        {
            ChatBot.Chat($"程序已重启。{Configuration.StartPrompt}");
        }

        return Task.CompletedTask;
    }
    protected override Task OnUpdate()
    {
        if (UpdateContext.FrameCount % (int)(1 / UpdateContext.ExpectedDeltaTime) != 0)
            return Task.CompletedTask;

        foreach ((DateTime time, Action action) in timeTask)
        {
            if (DateTime.Now > time)
                action();
        }
        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        await ChatBot.ChatAsync($"程序关闭中。{Configuration.DestroyPrompt}");
    }

    void OnChatSent(string message)
    {
        if (message.Contains(ChatBot.PokeMessageTag) == false)
        {
            continuousTimerCount = 0;
            NextTimer(); //重置自动报点
        }
    }

    int GetNextInterval(int layer, int shake)
    {
        int baseInterval = Configuration.UpdateInterval + shake;
        int multiplier = (int)MathF.Pow(
            Configuration.UpdateIntervalMultiplier,
            MathF.Min(layer, Configuration.UpdateMaxRetryCount));
        return baseInterval * multiplier;
    }
    void NextTimer()
    {
        int currentInterval = GetNextInterval(continuousTimerCount,
            Random.Shared.Next(-Configuration.UpdateRandomOffset, Configuration.UpdateRandomOffset));

        timeTask[0].Item1 = DateTime.Now.AddSeconds(currentInterval);
        timeTask[0].Item2 = () => {
            if (functionService.IsIdle == false)
                NextTimer(); //发生碰撞，重新尝试
            else
            {
                StringBuilder stringBuilder = new();
                stringBuilder.Append("系统周期报点。");
                stringBuilder.AppendLine(Configuration.UpdatePrompt);
                if (continuousTimerCount >= Configuration.UpdateMaxRetryCount)
                    stringBuilder.Append($"(系统周期报点已达最大间隔时间，如果你想重新活跃一段时间，请使用<{nameof(Awake)}>来重置周期报点)");

                interactor.Poke(stringBuilder.ToString());

                //配置下一次报点
                continuousTimerCount++;
                NextTimer();
            }
        };
    }
}