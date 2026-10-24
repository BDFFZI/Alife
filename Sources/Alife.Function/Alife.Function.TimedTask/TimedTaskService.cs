using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.SystemEvent;

[Module(
    "定时任务",
    "为AI提供等待与定点报时功能，用于主动控制日程、保持活跃。",
    defaultCategory: "Alife 官方/实用工具",
    EditorUI = typeof(TimedTaskServiceUI))]
public class TimedTaskService(
    XmlFunctionCaller functionCaller,
    ISystemEventService? systemEvent,
    Interactor<TimedTaskService> interactor) :
    ChatBehaviour,
    IConfigurable<TimedTaskServiceConfig>
{
    public TimedTaskServiceConfig Configuration { get; set; } = null!;

    public DateTime NextReportTime => pointTimerTime; //暴露给UI的数据
    public string Remark => pointTimerRemark; //暴露给UI的数据

    [XmlFunction(FunctionMode.OneShot)]
    [Description("让自己等待几秒再继续（通常仅用于主动追问或等待外部进程，因为内部工具通常支持回调，所以不需要使用）")]
    public async Task Await(int second, CancellationToken cancellationToken)
    {
        if (second > 60)
            throw new Exception($"不支持等待超过60秒，长时间等待请使用<{nameof(Awake)}>模拟");

        await Task.Delay(second * 1000, cancellationToken);

        interactor.Poke("AWait已完成");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("创建一个定点报时，同时重置系统周期报点（可以让你在目标时间段恢复活跃）")]
    public void Awake([Description("格式为ISO-8601")] DateTime time, string remark = "")
    {
        pointTimerTime = time;
        pointTimerRemark = remark;

        interactor.Poke($"已在 {time} 设置事件");
    }

    DateTime pointTimerTime = DateTime.MaxValue;
    string pointTimerRemark = "";

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "当你需要主动控制你的日程，想保持活跃时，请使用该功能。",
            Explanation = Configuration.Explanation
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, cancellationToken: DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override Task OnUpdate()
    {
        if (DateTime.Now > pointTimerTime)
        {
            interactor.Poke($"AWake报点：{pointTimerRemark}");
            pointTimerTime = DateTime.MaxValue; //关闭定时提醒
            systemEvent?.ResetTimer(); //触发时重置系统报点
        }

        return Task.CompletedTask;
    }
}