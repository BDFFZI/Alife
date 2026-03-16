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
        // 强制使用 UTF-8 编码进行 IPC 通讯
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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

            // 【关键新增】开启 IPC 监听任务
            _ = Task.Run(StartIpcListener);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
            Console.Error.WriteLine($"Pet Error: {ex.Message}");
        }
    }

    private void StartIpcListener()
    {
        while (true)
        {
            try
            {
                string? line = Console.ReadLine();
                if (line == null) break;
                
                // 将从 Host 接收到的命令转发给 WebView2
                Dispatcher.Invoke(() => {
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.PostWebMessageAsJson(line);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IPC Listener Error: {ex.Message}");
            }
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            // 将从 WebView2 接收到的事件转发给 Host
            Console.WriteLine(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            if (type == "drag-request")
            {
                Dispatcher.Invoke(() => {
                    var helper = new WindowInteropHelper(this);
                    ReleaseCapture();
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                });
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