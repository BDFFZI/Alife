using System;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

/// <summary>
/// 鼠标追踪管理器 - 负责鼠标事件与 WebView 交互
/// </summary>
public class MouseTracker
{
    private WebView2 webview;
    private GlobalMouseHook globalMouseHook;

    public MouseTracker(WebView2 webview)
    {
        this.webview = webview;
    }

    /// <summary>
    /// 初始化鼠标追踪
    /// </summary>
    public void Initialize()
    {
        globalMouseHook = new GlobalMouseHook();

        // 鼠标移动事件
        globalMouseHook.OnMouseMove += async (screenX, screenY) =>
        {
            try
            {
                if (webview?.CoreWebView2 == null) return;

                // 获取 WebView 窗口在屏幕上的位置
                var webViewPosition = webview.PointToScreen(new Point(0, 0));

                int localX = screenX - (int)webViewPosition.X;
                int localY = screenY - (int)webViewPosition.Y;

                // 检查鼠标是否在 WebView 范围内
                bool isInWebView = localX >= 0 && localX <= webview.ActualWidth &&
                                   localY >= 0 && localY <= webview.ActualHeight;

                var json = JsonSerializer.Serialize(new
                {
                    type = "mousemove",
                    x = localX,
                    y = localY,
                    isInWebView = isInWebView
                });

                await webview.CoreWebView2.ExecuteScriptAsync(
                    $"window.handleMouseMove({json});"
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"鼠标移动事件错误: {ex.Message}");
            }
        };

        // 鼠标点击事件
        globalMouseHook.OnMouseClick += async (screenX, screenY) =>
        {
            try
            {
                if (webview?.CoreWebView2 == null) return;

                var webViewPosition = webview.PointToScreen(new Point(0, 0));

                int localX = screenX - (int)webViewPosition.X;
                int localY = screenY - (int)webViewPosition.Y;

                var json = JsonSerializer.Serialize(new
                {
                    type = "click",
                    x = localX,
                    y = localY
                });

                await webview.CoreWebView2.ExecuteScriptAsync(
                    $"window.handleMouseClick({json});"
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"鼠标点击事件错误: {ex.Message}");
            }
        };

        globalMouseHook.Start();
    }

    /// <summary>
    /// 停止鼠标追踪
    /// </summary>
    public void Stop()
    {
        globalMouseHook?.Stop();
    }
}