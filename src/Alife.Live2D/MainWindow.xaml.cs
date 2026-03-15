using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Alife.Live2D;

public partial class MainWindow : Window
{
    private HttpCommandServer _server;

    public MainWindow()
    {
        Console.WriteLine("MainWindow: Constructor starting...");
        InitializeComponent();
        Console.WriteLine("MainWindow: InitializeComponent completed.");
        
        this.Loaded += (s, e) => {
            Console.WriteLine("MainWindow: Loaded event fired.");
            InitializeWebView();
        };

        _server = new HttpCommandServer(this);
        _server.Start();
        Console.WriteLine("MainWindow: Server started.");
    }

    public async void Say(string text)
    {
        if (webView.CoreWebView2 != null)
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"pet.say('{text.Replace("'", "\\'")}')");
        }
    }

    public async void SwitchModel(string modelName)
    {
        if (webView.CoreWebView2 != null)
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"pet.switch('{modelName.Replace("'", "\\'")}')");
        }
    }

    public async void SetThinking(bool isThinking)
    {
        if (webView.CoreWebView2 != null)
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"pet.setThinking({isThinking.ToString().ToLower()})");
        }
    }

    public async void DoAction(string action)
    {
        if (webView.CoreWebView2 != null)
        {
            string motionName = action switch
            {
                "开心" => "TapBody",
                "思考" => "Idle",
                "点点点" => "TapBody",
                "打招呼" => "TapBody",
                _ => action
            };
            await webView.CoreWebView2.ExecuteScriptAsync($"pet.action('{motionName}')");
        }
    }

    private async void InitializeWebView()
    {
        try 
        {
            Console.WriteLine("WebView2: Initialization starting...");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string wwwroot = Path.Combine(baseDir, "wwwroot");
            string htmlPath = Path.Combine(wwwroot, "index.html");
            
            Console.WriteLine($"WebView2: BaseDirectory: {baseDir}");
            Console.WriteLine($"WebView2: wwwroot: {wwwroot}");
            Console.WriteLine($"WebView2: Checking for index.html at {htmlPath}");

            if (!File.Exists(htmlPath))
            {
                Console.WriteLine($"WebView2 ERROR: index.html not found at {htmlPath}!");
                MessageBox.Show($"致命错误: 找不到资源文件 {htmlPath}\n请检查 wwwroot 是否已正确部署。");
                return;
            }

            string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alife.Live2D");
            Console.WriteLine($"WebView2: Creating environment with userDataFolder: {userDataFolder}");
            
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            Console.WriteLine("WebView2: Environment created successfully.");
            
            Console.WriteLine("WebView2: Ensuring CoreWebView2Async...");
            await webView.EnsureCoreWebView2Async(env);
            Console.WriteLine("WebView2: CoreWebView2 initialized.");
            
            // Register for process failure to catch crashes
            webView.CoreWebView2.ProcessFailed += (s, e) => {
                Console.WriteLine($"[WebView2] CRITICAL: Process Failed! Kind: {e.ProcessFailedKind}, Reason: {e.Reason}");
            };

            // 映射虚拟域名
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.pet", 
                wwwroot, 
                CoreWebView2HostResourceAccessKind.Allow);

            webView.WebMessageReceived += (s, e) => {
                try {
                    string json = e.WebMessageAsJson;
                    // Try to parse as structured object
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        string type = typeProp.GetString() ?? "unknown";
                        string msg = root.TryGetProperty("message", out var msgProp) ? (msgProp.GetString() ?? "") : "";

                        switch (type)
                        {
                            case "log":
                                Console.WriteLine($"[JS LOG] {msg}");
                                break;
                            case "error":
                                Console.WriteLine($"[JS ERROR] {msg}");
                                break;
                            case "warn":
                                Console.WriteLine($"[JS WARN] {msg}");
                                break;
                            case "command":
                                if (msg == "drag") this.Dispatcher.Invoke(() => this.DragMove());
                                break;
                        }
                    }
                    else
                    {
                        // Fallback for string messages
                        string msg = e.TryGetWebMessageAsString();
                        if (msg == "drag") this.DragMove();
                        else Console.WriteLine($"[JS MSG] {msg}");
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"[Message Error] Failed to parse JS message: {ex.Message}. Raw: {e.WebMessageAsJson}");
                }
            };

            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            
            Console.WriteLine("WebView2: Navigating to http://app.pet/index.html");
            webView.CoreWebView2.Navigate("http://app.pet/index.html");
            
            webView.CoreWebView2.NavigationStarting += (s, e) => {
                Console.WriteLine($"WebView2: Navigation starting to {e.Uri}");
            };

            webView.CoreWebView2.NavigationCompleted += (s, e) => {
                if (!e.IsSuccess) {
                    Console.WriteLine($"WebView2 ERROR: Navigation failed with status {e.WebErrorStatus}, HTTP: {e.HttpStatusCode}");
                }
                else {
                    Console.WriteLine("WebView2: Navigation completed successfully.");
                }
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebView2 CRITICAL ERROR: {ex.Message}");
            Console.WriteLine(ex.Source);
            Console.WriteLine(ex.StackTrace);
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            this.DragMove();
        }
    }
}