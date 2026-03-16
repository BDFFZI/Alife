using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
// 导入相关命名空间
// using Alife.Abstractions;
// using Alife.OfficialPlugins; // Decoupled

namespace Alife.Pet;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Debug.WriteLine("=== MainWindow Constructor ===");
        
        // 允许拖动窗口 (备用方案)
        this.MouseLeftButtonDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { this.DragMove(); } catch { }
            }
        };

        this.Loaded += (s, e) => InitializeWebView();
    }

    private async void InitializeWebView()
    {
        Debug.WriteLine("Starting WebView2 initialization...");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            Debug.WriteLine("WebView2 Core initialized.");

            // 注册消息接收
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 设置背景透明
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

            webView.Source = new Uri("https://app.local/index.html");
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // 后续由其他程序通过 WebView2 接口控制
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            if (type == "drag-request")
            {
                Dispatcher.Invoke(() => {
                    // 使用更加鲁棒的方法触发窗口拖动
                    var helper = new WindowInteropHelper(this);
                    ReleaseCapture(); // 关键：释放当前鼠标捕获
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                });
            }
            else if (type == "chat")
            {
                // Standalone 模式下不再处理 chat 逻辑
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebMessage processing error: {ex.Message}");
        }
    }

    // Windows API
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
}