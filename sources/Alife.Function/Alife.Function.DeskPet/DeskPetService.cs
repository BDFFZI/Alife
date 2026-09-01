using System;
using System.ComponentModel;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠 AI 前端：通过 <see cref="IDeskPet"/> 驱动任意桌宠显示实现（如 Live2D）。
/// 自身不管理桌宠生命周期，桌宠由具体实现（PetServer）自行管理。
/// </summary>
[Module("桌宠控制",
    """
    将AI连接到桌宠身体，使其可以用桌宠表达自己并接收互动反馈。
    """,
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(DeskPetServiceUI))]
public class DeskPetService(
    XmlFunctionCaller functionService,
    Interactor<DeskPetService> interactor,
    MessageFilterService messageFilterService,
    IDeskPet pet) :
    ChatBehaviour,
    IConfigurable<DeskPetServiceConfig>
{
    public DeskPetServiceConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.Content)]
    [Description("显示一段气泡文本")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Opening:
                    lastBubbleEndTime = 0;
                    break;
                case CallMode.Closing:
                {
                    try
                    {
                        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                            await Task.Delay(TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()),
                                cancellationToken);
                    }
                    finally
                    {
                        await pet.ShowSubtitle(null);
                    }
                    break;
                }
                case CallMode.Content:
                {
                    content = content.Trim();
                    if (string.IsNullOrWhiteSpace(content))
                        break;
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                        await Task.Delay(TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()),
                            cancellationToken);
                    await pet.ShowSubtitle(content);
                    lastBubbleEndTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() +
                                        Math.Max(content.Length * Configuration.BubbleDurationPerCharMs, 1500);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个表情（具体选项见附加说明）")]
    public Task Expression(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return Task.CompletedTask;
        if (pet.SupportedExpressions.Contains(option) == false)
            throw new Exception("选项不存在");

        return pet.ShowExpression(option);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个动作（具体选项见附加说明）")]
    public Task Motion(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return Task.CompletedTask;
        if (pet.SupportedMotions.Contains(option) == false)
            throw new Exception("选项不存在");

        return pet.ShowMotion(option);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取当前屏幕位置（使用后需等待结果返回）")]
    public async Task Position()
    {
        try
        {
            Vector2 position = await pet.GetPosition();
            interactor.Poke($"当前位置: x={position.X}, y={position.Y}");
        }
        catch (TimeoutException)
        {
            interactor.Poke("获取坐标超时");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在屏幕上进行相对移动（注意！该移动方式为相对位置移动，使用前最好先确认当前位置）")]
    public async Task Move(double x = 0, double y = 0, float seconds = 1)
    {
        await pet.Move(new Vector2((float)x, (float)y), seconds);
        Vector2 position = await pet.GetPosition();
        interactor.Poke($"移动成功，当前位置: x={position.X}, y={position.Y}");
    }

    long lastBubbleEndTime;
    bool lastStatus;

    protected override Task OnAwake()
    {
        //注册提示词
        string supportedExpressionsDescription = string.Join(", ", pet.SupportedExpressions);
        if (string.IsNullOrEmpty(supportedExpressionsDescription)) supportedExpressionsDescription = $"当前不支持<{nameof(Expression)}>功能";
        string supportedMotionsDescription = string.Join(", ", pet.SupportedMotions);
        if (string.IsNullOrEmpty(supportedMotionsDescription)) supportedMotionsDescription = $"当前不支持<{nameof(Motion)}>功能";
        XmlHandler xmlHandler = new(this) {
            Description = "此服务让你获得一副可交互的桌宠身体。这是你主要的对外输出表情动作等外观信息的工具，需要积极使用。",
            Explanation = $"""
                           ## 支持选项
                           - 支持的 {nameof(Expression)} 选项：{supportedExpressionsDescription}
                           - 支持的 {nameof(Motion)} 选项：{supportedMotionsDescription}
                           """
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        //注册消息回复规则
        messageFilterService.AddMessageReplyRule(new MessageReplyRule {
            Name = nameof(DeskPetService),
            InputMatching = input => input.Contains(Interactor<DeskPetService>.GetMessageTag()),
            OutputMatching = output => output.Contains(nameof(Speak), StringComparison.OrdinalIgnoreCase),
            CorrectionMessage = () => $"{nameof(DeskPetService)}消息必须用{nameof(Speak)}标签回复。如果不想发送消息，也请发送空标签。"
        }, DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override Task OnStart()
    {
        pet.OnInput += interactor.Chat;
        pet.OnInteracted += text => interactor.Chat("交互：" + text);

        return Task.CompletedTask;
    }
    protected override Task OnUpdate()
    {
        bool currentStatus = ChatBot.IsChatOccupied;
        if (currentStatus != lastStatus)
        {
            lastStatus = currentStatus;
            _ = pet.ShowUsing(currentStatus);
        }

        return Task.CompletedTask;
    }
}