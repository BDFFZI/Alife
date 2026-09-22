using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.SystemEvent;



[Module(
    "系统事件",
    "让AI可以获取到各种系统事件的提醒，包括周期性的主动唤醒。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = 100, //期望在末尾创建，以便获得栈顶事件顺序来发送启动关闭提醒
    EditorUI = typeof(SystemEventServiceUI))]
public class SystemEventService(
    XmlFunctionCaller functionService,
    Interactor<SystemEventService> interactor) :
    ChatBehaviour,
    IConfigurable<SystemEventServiceConfig>,
    ISystemEventService
{
    public SystemEventServiceConfig Configuration { get; set; } = null!;
    public DateTime NextReportTime => nextReportTime;
    public int CurrentReportCount => currentReportCount;

    /// <summary>
    /// 重置周期报点：清零连续触发次数并重新计时。供主动报点模块的 Awake 调用。
    /// </summary>
    public void ResetTimer()
    {
        currentReportCount = 0;
        NextTimer();
    }

    DateTime nextReportTime = DateTime.MaxValue;
    int currentReportCount;

    protected override Task OnStart()
    {
        //发送系统启动消息
        {
            OccupationMarker occupationMarker = ChatBot.LanguageModel.GetThinkingRequester().Rent("程序启动");

            if (ChatBot.ChatHistory.All(content => content.Role != AuthorRole.Assistant))
            {
                interactor.ChatAsync("""
                                     角色已激活：
                                     这是你第一次苏醒，初来乍到这个陌生环境，学习利用上下文中的工具了解这个世界。
                                     此外最重要的一件事，就是现在用上你丰富的能力，先向用户华丽的打个招呼吧！
                                     """)
                    .ContinueWith(_ => occupationMarker.Dispose());
            }
            else
            {
                interactor.ChatAsync($"程序已重启。{Configuration.StartPrompt}")
                    .ContinueWith(_ => occupationMarker.Dispose());
            }
        }

        ChatBot.ChatSent += OnChatSent;

        //设置定时
        NextTimer();

        return Task.CompletedTask;
    }
    protected override Task OnUpdate()
    {
        if (DateTime.Now > nextReportTime)
        {
            if (functionService.IsIdle)
            {
                StringBuilder stringBuilder = new();
                stringBuilder.Append("系统周期报点。");
                stringBuilder.AppendLine(Configuration.UpdatePrompt);
                if (currentReportCount >= Configuration.UpdateIntervalMaxPowerCount)
                    stringBuilder.Append("(系统周期报点已达最大间隔时间，如果你想重新活跃，请与主人进行任意一次对话)");

                interactor.Poke(stringBuilder.ToString());

                //提高报点间隔
                currentReportCount = Math.Min(currentReportCount + 1, Configuration.UpdateIntervalMaxPowerCount);
            }

            NextTimer();
        }

        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        ChatBot.ChatSent -= OnChatSent;

        await interactor.ChatAsync($"程序关闭中。{Configuration.DestroyPrompt}");
    }

    void OnChatSent(string message)
    {
        if (message.Contains(ChatBot.PokeMessageTag) == false)
        {
            ResetTimer();
        }
    }


    void NextTimer()
    {
        int shake = Random.Shared.Next(-Configuration.UpdateRandomOffset, Configuration.UpdateRandomOffset);
        int currentInterval = GetNextInterval(currentReportCount, shake);
        nextReportTime = DateTime.Now.AddSeconds(currentInterval);

        int GetNextInterval(int power, int shake)
        {
            int baseInterval = Configuration.UpdateInterval + shake;
            int multiplier = (int)MathF.Pow(Configuration.UpdateIntervalMultiplier, power);
            return baseInterval * multiplier;
        }
    }
}