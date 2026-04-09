using System.ComponentModel;
using Alife.Framework;
using Alife.Function.DeskPet;
using Alife.Function.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.Implement;

[Plugin("Live2D桌宠", "将Live2D桌宠接入AI系统，实现表现力同步和互动反馈。")]
[Description("此服务让你获得控制Live2D桌宠以及接收其交互的能力")]
public class DeskPetService : Plugin, IAsyncDisposable
{
    [XmlFunction("pbub")]
    [Description("气泡文字：显示一段浮动文字。示例: <pbub>你好</pbub>")]
    public void PetBubble(XmlExecutorContext context)
    {
        //流式输出模式：在内容更新时显示气泡
        if (context.CallMode == CallMode.Content && !string.IsNullOrWhiteSpace(context.FullContent))
        {
            client.ShowBubble(context.FullContent);
        }
        //标签结束模式：关闭气泡
        else if (context.CallMode == CallMode.Closing)
        {
            client.HideBubble();
        }
    }

    [XmlFunction("pexp")]
    [Description("控制表情：切换当前显示的表情。示例: <pexp>害羞</pexp>")]
    public void PetExpression(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        string expression = context.FullContent.Trim();
        client.PlayExpression(expression);
    }

    [XmlFunction("pmove")]
    [Description("移动位置：在屏幕上进行相对位移。示例: <pmove x=\"100\" y=\"50\" duration=\"3000\" /> - 表示向右移100像素，下移50像素")]
    public Task PetMove(XmlExecutorContext context, double x = 0, double y = 0, int duration = 1000)
    {
        if (context.CallMode != CallMode.OneShot)
            return Task.CompletedTask;

        if (x == 0 && y == 0 && string.IsNullOrEmpty(context.FullContent) == false)
        {
            string[] parts = context.FullContent.Split(',');
            if (parts.Length >= 2)
            {
                double.TryParse(parts[0].Trim(), out x);
                double.TryParse(parts[1].Trim(), out y);
            }
        }

        if (duration <= 0) duration = 1000;

        return client.MoveAsync(x, y, duration);
    }

    [XmlFunction("pmtn")]
    [Description("执行动作：播放预设动画。支持：害羞，摇头，点头；示例: <pmtn>害羞</pmtn>")]
    public void PetMotion(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;
        if (string.IsNullOrWhiteSpace(context.FullContent))
            return;

        string motion = context.FullContent.Trim();
        if (client.SupportedMotions.TryGetValue(motion, out (string Group, int Index) mtn))
        {
            client.PlayMotion(mtn.Group, mtn.Index);
        }
        else if (int.TryParse(motion, out int i))
        {
            client.PlayMotion("TapBody", i);
        }
    }

    [XmlFunction("pos")]
    [Description("获取位置：获取当前在屏幕上的绝对坐标。示例: <pos />")]
    public async Task PetPos(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.OneShot)
            return;

        try
        {
            (double x, double y) = await client.GetPositionAsync();
            chatBot.Poke($"[DeskPetService] 当前坐标: x={x}, y={y}");
        }
        catch (TimeoutException)
        {
            chatBot.Poke("[DeskPetService] 获取坐标超时");
        }
    }

    ChatBot chatBot = null!;
    PetServer client = null!;

    public DeskPetService(InterpreterService interpreterService)
    {
        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
        client = new PetServer();

        context.contextBuilder.ChatHistory.AddSystemMessage($"""
                                                             # DeskPetService 互动功能指南
                                                             你可以通过特殊标签控制你的互动表现，请根据对话情境使用：
                                                             1. **气泡文字**：`<pbub>内容</pbub>` (文本消息的视觉呈现。气泡会随内容流式更新，并在标签结束时自动消失)
                                                             2. **表情控制**：`<pexp>类型</pexp>`
                                                                - 支持：{string.Join(", ", client.SupportedExpressions)}
                                                             3. **动作控制**：`<pmtn>类型</pmtn>`
                                                                - 支持：{string.Join(", ", client.SupportedMotions.Keys)}
                                                             4. **生理反应 (Poke)**：
                                                                - 当你收到 `[DeskPetService] (物理干扰/连击干扰/交互: xxx) 台词` 格式的消息时，表示真央（桌宠）已经根据当前的物理刺激（点击、摇晃、连击）做出了“条件反射”式的本能反应（动作和基础台词）。
                                                                - 你作为真央的灵魂，应在此基础上进行情感化的后续回应，不要简单重复桌宠已经说过的本能反馈。
                                                             5. **获取位置**：`<pos />` (获取桌宠当前在屏幕上的坐标)
                                                             """);
        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;

        client.OnChat += text => chatBot.Chat("[DeskPetService] " + text);
        client.OnPoke += text => chatBot.Poke("[DeskPetService] " + text);

        chatActivity.ChatBot.ChatSent += _ => client.ResetInteractions();

        try
        {
            client.Start();
            Console.WriteLine("[DeskPetService] Pet process started successfully via Client.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeskPetService] Failed to start pet: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}
