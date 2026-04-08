using Alife.Basic;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Function.DeskPet;

public class MouseTracker
{
    public MouseTracker(MainWindow window)
    {
        this.window = window;
    }

    public void Initialize()
    {
        mouseHook = new GlobalMouseHook();

        mouseHook.MouseMove += (screenX, screenY) => {
            try
            {
                window.HandleMouseMoveRaw(screenX, screenY);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"鼠标移动事件错误: {ex.Message}");
            }
        };

        mouseHook.Start();
    }

    public void Stop()
    {
        mouseHook?.Stop();
    }

    readonly MainWindow window;
    GlobalMouseHook? mouseHook;
}
