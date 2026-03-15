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
        InitializeComponent();
        InitializeWebView();
        _server = new HttpCommandServer(this);
        _server.Start();

        webView.WebMessageReceived += (s, e) =>
        {
            if (e.TryGetWebMessageAsString() == "drag")
            {
                this.DragMove();
            }
        };
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
            string wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            string htmlPath = Path.Combine(wwwroot, "index.html");
            if (!File.Exists(htmlPath))
            {
                MessageBox.Show($"致命错误: 找不到资源文件 {htmlPath}\n请检查 wwwroot 是否已正确部署。");
                return;
            }

            string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alife.Live2D");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);
            
            // 映射虚拟域名，解决 file:/// 协议的各类安全限制和跨域问题
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.pet", 
                wwwroot, 
                CoreWebView2HostResourceAccessKind.Allow);

            // 保持调试用的右键菜单开启
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            
            // 使用虚拟域名导航
            webView.CoreWebView2.Navigate("http://app.pet/index.html");
            
            webView.CoreWebView2.NavigationCompleted += (s, e) => {
                if (!e.IsSuccess) {
                    // Log to console or handle quietly for production
                }
            };
        }
        catch 
        {
            // Silent catch or production logging
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