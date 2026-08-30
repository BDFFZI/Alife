using System;
using System.IO;
using System.Threading.Tasks;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Microsoft.Extensions.Logging;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠 Electron 浏览器窗口封装：负责创建透明置顶窗口、定位/移动/缩放、DPI 与脚本执行。
/// 网页资源位于插件 Resources/Live2D/（随插件分发或内嵌到客户端输出目录）。
/// </summary>
public sealed class PetWindow(ILogger<Live2DDeskPet> logger, string wwwRoot) : IDisposable
{
    public BrowserWindow? Window => window;

    public (double ScaleX, double ScaleY) GetDpi()
    {
        return dpi;
    }
    public (double Left, double Top, double Width, double Height) GetLayout()
    {
        BrowserWindow? w = window;
        if (w == null || w.IsDestroyedAsync().GetAwaiter().GetResult())
            return (0, 0, 0, 0);
        Rectangle bounds = w.GetBoundsAsync().GetAwaiter().GetResult();
        return (bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>
    /// 窗口中心点（物理像素，供 AI 汇报位置）。
    /// </summary>
    public (double X, double Y) GetCenterPosition()
    {
        (double left, double top, double width, double height) layout = GetLayout();
        return ((layout.left + layout.width / 2) * dpi.ScaleX, (layout.top + layout.height / 2) * dpi.ScaleY);
    }

    /// <summary>
    /// 按 DIP 增量移动窗口（拖拽用）。
    /// </summary>
    public void MoveBy(double dx, double dy)
    {
        BrowserWindow? w = window;
        if (w == null) return;
        try
        {
            int[] pos = w.GetPositionAsync().GetAwaiter().GetResult();
            w.SetPosition(pos[0] + (int)dx, pos[1] + (int)dy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "移动桌宠窗口失败");
        }
    }

    /// <summary>
    /// 按 DIP 增量调整窗口大小（右下角缩放用，最小 150）。
    /// </summary>
    public void ResizeBy(double dx, double dy)
    {
        BrowserWindow? w = window;
        if (w == null) return;
        try
        {
            Rectangle bounds = w.GetBoundsAsync().GetAwaiter().GetResult();
            int newWidth = Math.Max(MinSize, bounds.Width + (int)dx);
            int newHeight = Math.Max(MinSize, bounds.Height + (int)dy);
            w.SetBounds(new Rectangle { X = bounds.X, Y = bounds.Y, Width = newWidth, Height = newHeight });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "调整桌宠窗口大小失败");
        }
    }

    /// <summary>
    /// 平滑移动窗口（物理像素偏移，quadratic 缓动）。
    /// </summary>
    public async Task ProgrammaticMoveAsync(double offsetX, double offsetY, int durationMs)
    {
        BrowserWindow? w = window;
        if (w == null || await w.IsDestroyedAsync()) return;
        int[] start = await w.GetPositionAsync();
        double endX = start[0] + offsetX / dpi.ScaleX;
        double endY = start[1] + offsetY / dpi.ScaleY;
        long startTick = Environment.TickCount64;
        if (durationMs <= 0) durationMs = 1;
        while (true)
        {
            long elapsed = Environment.TickCount64 - startTick;
            double t = Math.Min(1.0, (double)elapsed / durationMs);
            double ease = t * (2 - t);
            w.SetPosition((int)Math.Round(start[0] + (endX - start[0]) * ease), (int)Math.Round(start[1] + (endY - start[1]) * ease));
            if (t >= 1.0) break;
            await Task.Delay(16);
        }
    }

    BrowserWindow? window;
    const int MinSize = 150;
    (double ScaleX, double ScaleY) dpi = (1.0, 1.0);

    public async Task CreateAsync()
    {
        string url = new Uri(Path.Combine(wwwRoot, "index.html")).AbsoluteUri;
        Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
        dpi = (primary.ScaleFactor, primary.ScaleFactor);

        window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
            Title = "Live2D 桌宠",
            Width = 400,
            Height = 600,
            X = primary.WorkArea.X + primary.WorkArea.Width - 440,
            Y = primary.WorkArea.Y + 40,
            AlwaysOnTop = true,
            SkipTaskbar = true,
            Frame = false,
            Transparent = true,
            HasShadow = false,
            AutoHideMenuBar = true,
            Resizable = true,
            Fullscreenable = false,
            BackgroundColor = "#00000000",
            WebPreferences = new WebPreferences {
                NodeIntegration = true,
                ContextIsolation = false,
                Sandbox = false,
                DevTools = true
            }
        }, url);
        window.OnReadyToShow += () => {
            //提升窗口置顶层级到最高（screen-saver），确保盖过全屏/无边框窗口。
            window.SetAlwaysOnTop(true, (OnTopLevel)7, 1);
            window.Show();
        };
    }
    public void Dispose()
    {
        window?.Destroy();
    }
}