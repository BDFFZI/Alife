using System;
using System.Collections.Generic;

namespace Alife.Function.DeskPet;

/// <summary>
/// 交互检测器：自注册鼠标移动事件驱动 <see cref="MotionDetector"/> 识别摇晃/位移/逗弄，
/// 并触发对应交互反馈（气泡+表情+动作）。不属于 IPetModule，仅作为 DI 组件。
/// </summary>
public class WindowInteractionModule : IPetModule
{
    readonly MotionDetector detector = new();
    readonly PetWindow window;
    readonly PetModelMetadata metadata;
    readonly SubtitleModule subtitleModule;
    readonly ExpressionModule expressionModule;
    readonly InteractedEventCallback onInteracted;

    public WindowInteractionModule(
        PetWindow window,
        PetModelMetadata metadata,
        SubtitleModule subtitleModule,
        ExpressionModule expressionModule,
        InteractedEventCallback onInteracted)
    {
        this.window = window;
        this.metadata = metadata;
        this.subtitleModule = subtitleModule;
        this.expressionModule = expressionModule;
        this.onInteracted = onInteracted;

        window.MouseMoved += OnMouseMoved;
        detector.WindowShaken += () => Play("window_shake", "大幅晃动");
        detector.WindowMoved += () => Play("window_move", "快速移动");
        detector.MouseShaken += () => Play("mouse_shake", "鼠标快速转圈");

        Play("startup", "启动");
    }
    void OnMouseMoved()
    {
        detector.Update(
            window.CursorScreenPoint.X, window.CursorScreenPoint.Y,
            window.Position.X, window.Bounds.Y + window.Bounds.Height * 0.3f,
            window.Bounds.X, window.Bounds.Y
        );
    }

    void Play(string type, string description)
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

/// <summary>
/// 干扰识别器：负责纯数学逻辑，识别摇摆、大幅位移以及鼠标逗弄
/// </summary>
public class MotionDetector
{
    public event Action? WindowShaken;
    public event Action? WindowMoved;
    public event Action? MouseShaken;

    public void Update(double mouseX, double mouseY, double centerX, double centerY, double windowLeft, double windowTop)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // 1. 处理窗口位移检测
        if (now - lastTime > ResetTimeWindow)
        {
            totalPath = 0;
            directionChanges = 0;
            isFirstWindowSample = true;
        }

        if (isFirstWindowSample)
        {
            lastLeft = windowLeft;
            lastTop = windowTop;
            startLeft = windowLeft;
            startTop = windowTop;
            isFirstWindowSample = false;
        }
        else
        {
            double dx = windowLeft - lastLeft;
            double dy = windowTop - lastTop;
            double stepDist = Math.Sqrt(dx * dx + dy * dy);

            if (stepDist >= 2)
            {
                totalPath += stepDist;
                if (lastDx != 0 && dx != 0 && Math.Sign(dx) != Math.Sign(lastDx)) directionChanges++;
                if (lastDy != 0 && dy != 0 && Math.Sign(dy) != Math.Sign(lastDy)) directionChanges++;

                lastLeft = windowLeft;
                lastTop = windowTop;
                lastDx = dx;
                lastDy = dy;

                // 计算净位移（起点到当前点的直线距离）
                double netDisplacement = Math.Sqrt(Math.Pow(windowLeft - startLeft, 2) + Math.Pow(windowTop - startTop, 2));

                // 1. 优先判定快速平移：总路程长且位移比率高（接近直线）
                if (totalPath > MovePathThreshold && netDisplacement > totalPath * 0.8)
                {
                    Reset();
                    WindowMoved?.Invoke();
                }
                // 2. 其次判定大幅晃动：
                // 条件 A: 变向次数达标（短频晃动）
                // 条件 B: 路程足够长但净位移很小（长程大幅折返）
                else if (totalPath > ShakePathThreshold &&
                         (directionChanges >= ShakeDirectionChanges || (totalPath > 1000 && netDisplacement < totalPath * 0.4)))
                {
                    Reset();
                    WindowShaken?.Invoke();
                }
            }
        }
        lastTime = now;

        // 2. 处理鼠标逗弄检测 (改为角度旋转追踪)
        double dxMouse = mouseX - centerX;
        double dyMouse = mouseY - centerY;
        double distToCenter = Math.Sqrt(dxMouse * dxMouse + dyMouse * dyMouse);

        if (now - lastMouseTime > ResetTimeWindow)
        {
            ResetMouseTrack();
        }

        // 半径限制：防止在屏幕对角线误触，核心是判定是否绕着中心转
        if (distToCenter > MouseSensitiveRadius) return;

        double currentAngle = Math.Atan2(dyMouse, dxMouse);

        if (isFirstMouseSample)
        {
            lastMouseAngle = currentAngle;
            isFirstMouseSample = false;
        }
        else
        {
            // 计算弧度增量
            double deltaAngle = currentAngle - lastMouseAngle;

            // 处理 Atan2 的回绕问题 ( -PI 到 PI )
            if (deltaAngle > Math.PI) deltaAngle -= 2 * Math.PI;
            else if (deltaAngle < -Math.PI) deltaAngle += 2 * Math.PI;

            accumulatedRotation += deltaAngle;

            // 触发阈值：6 圈 (6 * 2 * PI ≈ 37.7 弧度)
            if (Math.Abs(accumulatedRotation) >= RotationThreshold)
            {
                ResetMouseTrack();
                MouseShaken?.Invoke();
            }
        }
        lastMouseAngle = currentAngle;
        lastMouseTime = now;
    }

    const double ShakePathThreshold = 600;
    const int ShakeDirectionChanges = 4;
    const double MovePathThreshold = 1200;
    const int ResetTimeWindow = 400;

    const double MouseSensitiveRadius = 6000;
    const double RotationThreshold = 5.0 * 2.0 * Math.PI;

    double lastLeft;
    double lastTop;
    double startLeft;
    double startTop;
    double totalPath;
    int directionChanges;
    double lastDx;
    double lastDy;
    long lastTime;
    bool isFirstWindowSample = true;
    bool isFirstMouseSample = true;

    double lastMouseAngle;
    double accumulatedRotation;
    long lastMouseTime;

    void Reset()
    {
        totalPath = 0;
        directionChanges = 0;
        lastDx = 0;
        lastDy = 0;
        isFirstWindowSample = true;
    }

    void ResetMouseTrack()
    {
        accumulatedRotation = 0;
        isFirstMouseSample = true;
    }
}