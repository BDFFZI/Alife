using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Color = System.Drawing.Color;

namespace Alife.Client.Windows;

[SuppressMessage("ReSharper", "InconsistentNaming")]
sealed partial class WindowsAlifeWindow
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(nint hWnd, int nIndex);
    [DllImport("user32.dll")]
    static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}

sealed partial class WindowsAlifeWindow : Form, IAlifeWindow
{
    public event Action<string>? MessageReceived;

    public async Task Launch()
    {
        if (InvokeRequired)
        {
            await Invoke(Launch);
            return;
        }
        Show(); //窗口激活后webview才能初始化

        webView = new WebView2 {
            DefaultBackgroundColor = options.Transparent ? Color.Transparent : Color.White,
            Dock = DockStyle.Fill,
        };

        CoreWebView2EnvironmentOptions environmentOptions = new() {
            AdditionalBrowserArguments = "--allow-file-access-from-files",
        }; //允许访问任意位置的文件
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            options: environmentOptions
        );
        await webView.EnsureCoreWebView2Async(environment);

        CoreWebView2 core = webView.CoreWebView2;
        core.WebMessageReceived += OnWebMessageReceived;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);

        Uri navigateTo = NormalizeUrl(options.Url);
        core.Navigate(navigateTo.ToString());

        Controls.Add(webView);
        static Uri NormalizeUrl(string url)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            if (Path.IsPathRooted(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                string path = Path.GetFullPath(url);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Window HTML file not found.", path);
                url = new Uri(path).AbsoluteUri;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
                (target.Scheme is not ("http" or "https" or "file") && url != "about:blank"))
                throw new NotSupportedException("Web windows require an absolute HTTP, HTTPS or file URL, or about:blank.");
            return target;
        }
    }

    public Task ShowAsync()
    {
        if (InvokeRequired)
        {
            Invoke(() => {
                Show();
                Activate();
            });
            return Task.CompletedTask;
        }
        Show();
        Activate();
        return Task.CompletedTask;
    }
    public Task HideAsync()
    {
        if (InvokeRequired)
        {
            Invoke(Hide);
            return Task.CompletedTask;
        }
        Hide();
        return Task.CompletedTask;
    }

    public Task SetBoundsAsync(Rectangle bounds)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SetBoundsAsync(bounds)));
            return Task.CompletedTask;
        }

        ClientSize = (bounds.Size * dpiScale).ToSize();
        Location = new Point(
            (int)(bounds.Location.X * dpiScale - clientOffsetPs.X),
            (int)(bounds.Location.Y * dpiScale - clientOffsetPs.Y)
        );
        return Task.CompletedTask;
    }
    public Task SetTopMostAsync(bool topMost)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => TopMost = topMost));
            return Task.CompletedTask;
        }
        TopMost = topMost;
        return Task.CompletedTask;
    }
    public Task SetIgnoreMouseEventsAsync(bool ignore)
    {
        int style = GetWindowLong(Handle, GWL_EXSTYLE);
        int next = ignore ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (next != style)
            SetWindowLong(Handle, GWL_EXSTYLE, next);
        return Task.CompletedTask;
    }

    public Task PostMessageAsync(string messageJson)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => PostMessageAsync(messageJson)));
            return Task.CompletedTask;
        }
        WebView2? view = webView;
        if (view == null)
            throw new NotSupportedException("Message channel is only supported on Html windows.");
        view.CoreWebView2.PostWebMessageAsJson(messageJson);
        return Task.CompletedTask;
    }
    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (InvokeRequired)
        {
            var result = Invoke(() => ExecuteScriptAsync(script));
            return await result;
        }
        WebView2? view = webView;
        if (view == null)
            throw new NotSupportedException("Script execution is only supported on Html windows.");
        TaskCompletionSource<string?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await view.CoreWebView2.ExecuteScriptAsync(script).ContinueWith(t => {
            if (t.IsFaulted)
                tcs.TrySetException(t.Exception?.InnerException ?? t.Exception!);
            else if (t.IsCanceled)
                tcs.TrySetCanceled();
            else
                tcs.TrySetResult(t.Result);
        }, TaskScheduler.Default);
        return await tcs.Task;
    }

    readonly AlifeWindowOptions options;
    readonly float dpiScale;
    WebView2? webView;
    const string BridgeScript = """
                                (function () {
                                    if (window.alifeWindow) return;
                                    const handlers = [];
                                    function postMessage(data) {
                                        try {
                                            window.chrome.webview.postMessage(
                                                typeof data === 'string' ? data : JSON.stringify(data)
                                            );
                                        } catch { }
                                    }
                                    window.alifeWindow = {
                                        postMessage,
                                        onMessage(handler) {
                                            handlers.push(handler);
                                        },
                                        _dispatch(msg) {
                                            handlers.forEach(function (handler) {
                                                try {
                                                    handler(msg);
                                                } catch (e) {
                                                    console.error('[alifeWindow] handler error:', e);
                                                }
                                            });
                                        },
                                    };
                                    try {
                                        window.chrome.webview.addEventListener('message', function (event) {
                                            let msg = event.data;
                                            if (typeof msg === 'string') {
                                                try { msg = JSON.parse(msg); } catch { return; }
                                            }
                                            window.alifeWindow._dispatch(msg);
                                        });
                                    } catch { }
                                })();
                                """;
    readonly Point clientOffsetPs;

    public WindowsAlifeWindow(AlifeWindowOptions options)
    {
        this.options = options;

        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        dpiScale = DeviceDpi / 96f;

        Text = options.Title;
        FormBorderStyle = options.Transparent ? FormBorderStyle.None : options.Resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
        BackColor = options.Transparent ? Color.Red : Color.White;
        TransparencyKey = options.Transparent ? Color.Red : Color.Empty; //当透明掩码的 R!=G 时，可以透明的同时保持鼠标事件
        TopMost = options.TopMost;
        ShowInTaskbar = false;
        ControlBox = false;

        Point clientOrigin = PointToScreen(Point.Empty);
        clientOffsetPs = new Point(
            clientOrigin.X - Location.X,
            clientOrigin.Y - Location.Y
        );

        if (options.Bounds is { } bounds)
        {
            StartPosition = FormStartPosition.Manual;
            SetBoundsAsync(bounds);
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }

        string iconPath = Path.Combine(AppContext.BaseDirectory, "alife-icon.ico");
        if (File.Exists(iconPath))
            Icon = new Icon(iconPath);
    }
    public ValueTask DisposeAsync()
    {
        if (InvokeRequired)
        {
            Invoke(DisposeAsync);
            return ValueTask.CompletedTask;
        }
        if (webView != null)
        {
            webView.Dispose();
            webView = null;
        }
        Close();
        return ValueTask.CompletedTask;
    }

    void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message = e.TryGetWebMessageAsString();
        MessageReceived?.Invoke(message ?? e.WebMessageAsJson);
    }
}