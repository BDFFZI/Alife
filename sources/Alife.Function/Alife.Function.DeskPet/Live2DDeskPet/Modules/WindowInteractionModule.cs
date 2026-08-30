using System;
using System.Collections.Generic;

namespace Alife.Function.DeskPet;

/// <summary>
/// 交互检测器：自注册鼠标移动事件驱动 <see cref="MotionDetector"/> 识别摇晃/位移/逗弄，
/// 并触发对应交互反馈（气泡+表情+动作）。不属于 IPetModule，仅作为 DI 组件。
/// </summary>
public class WindowInteractionModule : IPetModule
{
    readonly PetModelMetadata metadata;
    readonly SubtitleModule subtitleModule;
    readonly ExpressionModule expressionModule;
    readonly InteractedEventCallback onInteracted;

    public WindowInteractionModule(
        MouseTracker tracker,
        PetWindow window,
        MotionDetector detector,
        PetModelMetadata metadata,
        SubtitleModule subtitleModule,
        ExpressionModule expressionModule,
        InteractedEventCallback onInteracted)
    {
        this.metadata = metadata;
        this.subtitleModule = subtitleModule;
        this.expressionModule = expressionModule;
        this.onInteracted = onInteracted;
        tracker.MouseMoved += (x, y) => {
            (double scaleX, double scaleY) dpi = window.GetDpi();
            double windowMouseX = x / dpi.scaleX;
            double windowMouseY = y / dpi.scaleY;
            (double left, double top, double width, double height) layout = window.GetLayout();
            detector.Update(windowMouseX, windowMouseY, layout.left + layout.width / 2, layout.top + layout.height / 2, layout.left, layout.top);
        };
        detector.WindowShaken += () => Play("window_shake", "大幅晃动");
        detector.WindowMoved += () => Play("window_move", "快速移动");
        detector.MouseShaken += () => Play("mouse_shake", "鼠标快速转圈");
    }

    public void Play(string type, string description)
    {
        onInteracted.Invoke($"桌宠被{description}");

        if (metadata.Interactions.TryGetValue(type, out List<InteractionItem>? pool) == false || pool.Count == 0)
            return;

        InteractionItem item = pool[Random.Shared.Next(pool.Count)];
        if (string.IsNullOrEmpty(item.Text) == false)
            subtitleModule.Show(item.Text);
        if (string.IsNullOrEmpty(item.Exp) == false)
            expressionModule.PlayExpression(item.Exp);
        if (item.Mtn != null)
            expressionModule.PlayMotion(item.Mtn.Group, item.Mtn.Index);
    }
}