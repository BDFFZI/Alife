using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

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
        
        // 确保在加载前设置基本属性
        this.Loaded += (s, e) => {
            Debug.WriteLine("=== Window Loaded ===");
            InitializeWebView();
        };

        // 允许拖动窗口
        this.MouseLeftButtonDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        };
    }

    private async void InitializeWebView()
    {
        Debug.WriteLine("Starting WebView2 initialization...");
        try
        {
            // 环境设置
            var env = await CoreWebView2Environment.CreateAsync();
            Debug.WriteLine("CoreWebView2Environment created.");
            
            await webView.EnsureCoreWebView2Async(env);
            Debug.WriteLine("WebView2 Core initialized.");

            // 设置背景透明
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            // 诊断事件
            webView.CoreWebView2.NavigationStarting += (s, e) => Debug.WriteLine($"Navigation Starting: {e.Uri}");
            webView.CoreWebView2.NavigationCompleted += (s, e) => Debug.WriteLine($"Navigation Completed: {e.IsSuccess} ({e.WebErrorStatus})");
            webView.CoreWebView2.ProcessFailed += (s, e) => Debug.WriteLine($"Process Failed: {e.ProcessFailedKind}");

            // 映射本地文件到虚拟主机
            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwroot))
            {
                Debug.WriteLine($"ERROR: wwwroot directory not found at {wwwroot}");
                MessageBox.Show($"wwwroot 目录丢失: {wwwroot}");
            }

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            Debug.WriteLine("Host mapping set.");

            webView.Source = new Uri("https://app.local/index.html");
            Debug.WriteLine("WebView2 Source set to app.local.");
            
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"WebView2 初始化失败: {ex.Message}");
        }
    }
}