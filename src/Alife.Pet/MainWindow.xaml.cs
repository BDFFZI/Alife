using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;

// 导入相关命名空间
using Alife;
using Alife.Abstractions;
using Alife.OfficialPlugins;

namespace Alife.Pet;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // 全局命名空间中的类型
    private global::ChatActivity? _chatActivity;
    private global::ChatWindow? _chatWindow;

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

            // 初始化 AI
            await InitializeAIAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task InitializeAIAsync()
    {
        try
        {
            Debug.WriteLine("Initializing AI Mao...");
            
            // 确保官方插件程序集已加载
            try { Assembly.Load("Alife.OfficialPlugins"); } catch { }

            // 1. 初始化核心系统
            var storage = new Alife.StorageSystem();
            var config = new global::ConfigurationSystem(storage);
            
            // 2. 加载 Mao 人设 (使用绝对路径或备用字符串)
            string personaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                @".gemini\antigravity\brain\c24b2c36-4430-4055-8c98-19bc81735cfd\mao_persona_prompt.md");
            
            string prompt = File.Exists(personaPath) ? File.ReadAllText(personaPath) : "你是一个名为 Mao (真央) 的助理。";

            var character = new global::Character {
                Name = "Mao",
                Prompt = prompt,
                Plugins = new HashSet<Type> {
                    typeof(Alife.OfficialPlugins.InterpreterService),
                    typeof(Alife.OfficialPlugins.SpeechService),
                    typeof(global::ChatWindow)
                }
            };

            // 3. 创建聊天活动
            _chatActivity = await global::ChatActivity.Create(character, config, null, [config, storage]);
            
            // 4. 获取 ChatWindow 并订阅消息
            _chatWindow = _chatActivity.PluginService.GetRequiredService<global::ChatWindow>();
            _chatWindow.MessageAdded += (msg) => {
                if (msg.tool == "speak" && !msg.isUser)
                {
                    Dispatcher.Invoke(() => {
                        string escaped = JsonEncodedText.Encode(msg.content ?? "").ToString();
                        webView.CoreWebView2.ExecuteScriptAsync($"if(window.showBubble) showBubble(\"{escaped}\")");
                    });
                }
            };

            Debug.WriteLine("AI Mao initialized successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AI Init Error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
                    if (Mouse.LeftButton == MouseButtonState.Pressed)
                    {
                        try { this.DragMove(); } catch { }
                    }
                });
            }
            else if (type == "chat")
            {
                if (root.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString();
                    if (_chatActivity != null && !string.IsNullOrEmpty(text))
                    {
                        Debug.WriteLine($"User inputs: {text}");
                        // 使用正确的 ChatAsync 方法
                        await _chatActivity.ChatBot.ChatAsync(text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebMessage processing error: {ex.Message}");
        }
    }
}