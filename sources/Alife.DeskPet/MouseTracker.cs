using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Alife.Pet;

public class MouseTracker
{
    public MouseTracker(WebView2 webView)
    {
        this.webView = webView;
    }

    public void Initialize()
    {
        mouseHook = new GlobalMouseHook();

        mouseHook.MouseMove += async (screenX, screenY) => {
            try
            {
                if (webView.CoreWebView2 == null)
                    return;

                Point webViewPosition = webView.PointToScreen(new Point(0, 0));

                int localX = screenX - (int)webViewPosition.X;
                int localY = screenY - (int)webViewPosition.Y;

                bool isInWebView = localX >= 0 && localX <= webView.ActualWidth &&
                                   localY >= 0 && localY <= webView.ActualHeight;

                string json = JsonSerializer.Serialize(new {
                    type = "mousemove",
                    x = localX,
                    y = localY,
                    isInWebView = isInWebView
                });

                await webView.CoreWebView2.ExecuteScriptAsync($"window.handleMouseMove({json});");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"鼠标移动事件错误: {ex.Message}");
            }
        };

        mouseHook.MouseClick += async (screenX, screenY) => {
            try
            {
                if (webView.CoreWebView2 == null)
                    return;

                Point webViewPosition = webView.PointToScreen(new Point(0, 0));

                int localX = screenX - (int)webViewPosition.X;
                int localY = screenY - (int)webViewPosition.Y;

                string json = JsonSerializer.Serialize(new {
                    type = "click",
                    x = localX,
                    y = localY
                });

                await webView.CoreWebView2.ExecuteScriptAsync($"window.handleMouseClick({json});");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"鼠标点击事件错误: {ex.Message}");
            }
        };

        mouseHook.Start();
    }

    public void Stop()
    {
        mouseHook?.Stop();
    }

    WebView2 webView;
    GlobalMouseHook? mouseHook;
}