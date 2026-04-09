using System;
using System.Collections.Generic;

namespace Alife.Function.DeskPet;

/// <summary>
/// 干扰识别器：负责纯数学逻辑，识别摇摆、大幅位移以及鼠标逗弄
/// </summary>
public class InterferenceDetector
{
    public event Action? Shaked;
    public event Action? Moved;
    public event Action? MouseShaked;

    public void ReportLocation(double left, double top)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        if (now - lastTime > ResetTimeWindow)
        {
            totalPath = 0;
            directionChanges = 0;
        }

        double dx = left - lastLeft;
        double dy = top - lastTop;
        double stepDist = Math.Sqrt(dx * dx + dy * dy);

        if (stepDist < 2) return;

        totalPath += stepDist;

        if (lastDx != 0 && Math.Sign(dx) != Math.Sign(lastDx)) directionChanges++;
        if (lastDy != 0 && Math.Sign(dy) != Math.Sign(lastDy)) directionChanges++;

        lastLeft = left;
        lastTop = top;
        lastDx = dx;
        lastDy = dy;
        lastTime = now;

        if (totalPath > ShakePathThreshold && directionChanges >= ShakeDirectionChanges)
        {
            Reset();
            Shaked?.Invoke();
        }
        else if (totalPath > MovePathThreshold && directionChanges < MoveMinDirectionChanges)
        {
            Reset();
            Moved?.Invoke();
        }
    }

    /// <summary>
    /// 上报鼠标坐标，用于检测“逗弄（鼠标在周围摇晃）”行为
    /// </summary>
    public void ReportMouseLocation(double mx, double my, double cx, double cy)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // 1. 距离校验：鼠标必须在中心感应范围内
        double dx = mx - cx;
        double dy = my - cy;
        double distToCenter = Math.Sqrt(dx * dx + dy * dy);

        if (distToCenter > MouseSensitiveRadius)
        {
            ResetMouseTrack();
            return;
        }

        // 2. 超时重置
        if (now - lastMouseTime > ResetTimeWindow)
        {
            ResetMouseTrack();
        }

        // 3. 轨迹分析
        double dmx = mx - lastMx;
        double dmy = my - lastMy;
        double stepDist = Math.Sqrt(dmx * dmx + dmy * dmy);

        if (stepDist < 2) return;

        mouseTotalPath += stepDist;

        if (lastDmx != 0 && Math.Sign(dmx) != Math.Sign(lastDmx)) mouseDirectionChanges++;
        if (lastDmy != 0 && Math.Sign(dmy) != Math.Sign(lastDmy)) mouseDirectionChanges++;

        lastMx = mx;
        lastMy = my;
        lastDmx = dmx;
        lastDmy = dmy;
        lastMouseTime = now;

        // 4. 阈值触发
        if (mouseTotalPath > MouseShakePathThreshold && mouseDirectionChanges >= MouseShakeDirectionChanges)
        {
            ResetMouseTrack();
            MouseShaked?.Invoke();
        }
    }

    const double ShakePathThreshold = 1000;
    const int ShakeDirectionChanges = 4;
    const double MovePathThreshold = 5000;
    const int MoveMinDirectionChanges = 2;
    const int ResetTimeWindow = 300;

    const double MouseSensitiveRadius = 300;
    const double MouseShakePathThreshold = 800;
    const int MouseShakeDirectionChanges = 4;

    double lastLeft;
    double lastTop;
    double totalPath;
    int directionChanges;
    double lastDx;
    double lastDy;
    long lastTime;

    double lastMx;
    double lastMy;
    double lastDmx;
    double lastDmy;
    double mouseTotalPath;
    int mouseDirectionChanges;
    long lastMouseTime;

    void Reset()
    {
        totalPath = 0;
        directionChanges = 0;
        lastDx = 0;
        lastDy = 0;
    }

    void ResetMouseTrack()
    {
        mouseTotalPath = 0;
        mouseDirectionChanges = 0;
        lastDmx = 0;
        lastDmy = 0;
    }
}
