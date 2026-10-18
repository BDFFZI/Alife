using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Webkit;

namespace Alife.Client.Android;

/// <summary>
/// 插件动态窗口：悬浮窗（TopMost）或应用内对话框，承载 WebView 内容。
/// 进程内直接实现，消息经事件回传给插件。
/// </summary>
sealed class AndroidWebWindow : Alife.Client.IAlifeWindow
{
    public AndroidWebWindow(Alife.Client.AlifeWindowOptions options, string windowId)
    {
        this.options = options;
        this.windowId = windowId;
        crossApplication = options.TopMost;

        Context context = crossApplication
            ? MainApplication.Instance!
            : MainActivity.Current ?? throw new InvalidOperationException("主界面未打开，无法创建窗口。");
        density = context.Resources!.DisplayMetrics!.Density;
        manager = global::Android.Runtime.Extensions.JavaCast<IWindowManager>(context.GetSystemService(Context.WindowService))!;
        view = CreateWebView(context, allowFileAccess: true);
        view.SetBackgroundColor(options.Transparent ? global::Android.Graphics.Color.Transparent : global::Android.Graphics.Color.White);
        view.AddJavascriptInterface(new JsBridge(this), BridgeName);
        view.SetWebViewClient(new WindowClient());

        if (crossApplication)
        {
            windowLayout = new WindowManagerLayoutParams(0, 0,
                WindowManagerTypes.ApplicationOverlay,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent) {
                Gravity = GravityFlags.Top | GravityFlags.Start,
            };
        }
        else
        {
            dialog = new Dialog(context);
            if (options.Transparent)
                dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
            else
                dialog.SetTitle(options.Title);
            dialog.SetContentView(view);
            dialog.SetCanceledOnTouchOutside(false);
        }

        if (options.Bounds is { } initialBounds)
            SetBounds(initialBounds);
        Navigate(options.Url);
    }

    public event Action<string>? MessageReceived;
    public event Action? Disposed;

    public string WindowId => windowId;

    public Task Launch() => RunOnUi(Show);
    public Task ShowAsync() => RunOnUi(Show);
    public Task HideAsync() => RunOnUi(Hide);
    public Task SetBoundsAsync(System.Drawing.Rectangle bounds) => RunOnUi(() => SetBounds(bounds));
    public Task SetTopMostAsync(bool topMost) => Task.CompletedTask; //窗口类型创建时已确定
    public Task SetIgnoreMouseEventsAsync(bool ignore) => RunOnUi(() => SetIgnoreInput(ignore));
    public Task PostMessageAsync(string messageJson) => RunOnUi(() => PostMessage(messageJson));
    public Task<string?> ExecuteScriptAsync(string script) => RunOnUi(() => ExecuteScriptCoreAsync(script)).Unwrap();

    public ValueTask DisposeAsync()
    {
        RunOnUi(DisposeSync);
        return ValueTask.CompletedTask;
    }

    static Task RunOnUi(Action action)
    {
        global::Android.OS.Handler handler = new(global::Android.OS.Looper.MainLooper!);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Post(() => {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });
        return completion.Task;
    }

    static Task<T> RunOnUi<T>(Func<T> function)
    {
        global::Android.OS.Handler handler = new(global::Android.OS.Looper.MainLooper!);
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Post(() => {
            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });
        return completion.Task;
    }

    Task<string?> ExecuteScriptCoreAsync(string script)
    {
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.EvaluateJavascript(script, new ScriptCallback(completion));
        return completion.Task;
    }

    void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (visible)
            return;

        if (crossApplication)
        {
            if (Settings.CanDrawOverlays(view.Context) == false)
            {
                Intent request = new(Settings.ActionManageOverlayPermission,
                    global::Android.Net.Uri.Parse($"package:{view.Context!.PackageName}"));
                request.AddFlags(ActivityFlags.NewTask);
                view.Context.StartActivity(request);
                throw new UnauthorizedAccessException("请授予 Alife 悬浮窗权限后重试。");
            }
            manager.AddView(view, windowLayout!);
        }
        else
        {
            dialog!.Show();
            ApplyDialogBounds();
        }
        visible = true;
    }

    void Hide()
    {
        if (visible == false)
            return;
        if (crossApplication)
            manager.RemoveView(view);
        else
            dialog!.Hide();
        visible = false;
    }

    void SetBounds(System.Drawing.Rectangle bounds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        x = Scale(bounds.X);
        y = Scale(bounds.Y);
        width = Scale(bounds.Width);
        height = Scale(bounds.Height);

        global::Android.Util.DisplayMetrics? metrics = view.Context?.Resources?.DisplayMetrics;
        if (metrics != null)
        {
            x = Math.Clamp(x, 0, Math.Max(0, metrics.WidthPixels - width));
            y = Math.Clamp(y, 0, Math.Max(0, metrics.HeightPixels - height));
        }

        if (crossApplication)
        {
            WindowManagerLayoutParams layout = windowLayout!;
            layout.Width = width;
            layout.Height = height;
            layout.X = x;
            layout.Y = y;
            if (visible)
                manager.UpdateViewLayout(view, layout);
        }
        else if (visible)
        {
            ApplyDialogBounds();
        }
    }

    void SetIgnoreInput(bool ignore)
    {
        if (crossApplication == false)
            return;
        WindowManagerLayoutParams layout = windowLayout!;
        WindowManagerFlags next = ignore
            ? layout.Flags | WindowManagerFlags.NotTouchable | WindowManagerFlags.NotFocusable
            : layout.Flags & ~WindowManagerFlags.NotTouchable;
        if (next == layout.Flags)
            return;
        layout.Flags = next;
        if (visible)
            manager.UpdateViewLayout(view, layout);
    }

    void PostMessage(string payload)
    {
        string script = "(function(){try{if(window.alifeWindow)window.alifeWindow._dispatch("
            + payload
            + ");}catch(e){console.error('[alifeWindow] dispatch failed:',e);}})();";
        view.Post(() => view.EvaluateJavascript(script, null));
    }

    void DisposeSync()
    {
        if (disposed)
            return;
        Hide();
        disposed = true;
        dialog?.Dismiss();
        dialog?.Dispose();
        dialog = null;
        view.RemoveJavascriptInterface(BridgeName);
        view.Destroy();
        view.Dispose();
        Disposed?.Invoke();
    }

    void Navigate(string url)
    {
        Uri target;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute))
        {
            target = absolute;
        }
        else if (System.IO.Path.IsPathRooted(url))
        {
            string path = System.IO.Path.GetFullPath(url);
            if (File.Exists(path) == false)
                throw new FileNotFoundException("Window HTML file not found.", path);
            target = new Uri(path);
        }
        else
        {
            throw new NotSupportedException("相对路径窗口需要提供基准地址，请使用绝对 URL 或文件路径。");
        }

        if (target.Scheme is not ("http" or "https" or "file") && url != "about:blank")
            throw new NotSupportedException("Web windows require an absolute HTTP, HTTPS or file URL, or about:blank.");
        view.Settings.AllowFileAccess = target.IsFile;
        view.Settings.AllowFileAccessFromFileURLs = target.IsFile;
        view.LoadUrl(target.ToString());
    }

    void ApplyDialogBounds()
    {
        global::Android.Views.Window window = dialog!.Window!;
        window.SetGravity(GravityFlags.Top | GravityFlags.Start);
        WindowManagerLayoutParams attributes = window.Attributes!;
        attributes.X = x;
        attributes.Y = y;
        attributes.Width = width;
        attributes.Height = height;
        window.Attributes = attributes;
        if (options.Transparent)
        {
            window.SetBackgroundDrawable(new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent));
            window.SetDimAmount(0);
            window.ClearFlags(WindowManagerFlags.DimBehind);
        }
    }

    int Scale(int value)
    {
        return (int)Math.Round(value * density);
    }

    static global::Android.Webkit.WebView CreateWebView(Context context, bool allowFileAccess)
    {
#if DEBUG
        global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
#endif
        global::Android.Webkit.WebView created = new(context);
        WebSettings settings = created.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = allowFileAccess;
        settings.AllowFileAccessFromFileURLs = allowFileAccess;
        settings.AllowContentAccess = false;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.SetSupportMultipleWindows(false);
        settings.BuiltInZoomControls = false;
        settings.DisplayZoomControls = false;
        created.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
        return created;
    }

    sealed class ScriptCallback(TaskCompletionSource<string?> completion) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            completion.TrySetResult(value?.ToString());
        }
    }

    sealed class JsBridge(AndroidWebWindow window) : Java.Lang.Object
    {
        [JavascriptInterface]
        [Java.Interop.Export("postMessage")]
        public void PostMessage(string message)
        {
            window.MessageReceived?.Invoke(message);
        }
    }

    sealed class WindowClient : WebViewClient
    {
        public override void OnPageStarted(global::Android.Webkit.WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            view?.EvaluateJavascript(BridgeScript, null);
        }
    }

    const string BridgeName = "AlifeAndroidHost";
    const string BridgeScript = """
                                (function () {
                                    if (window.alifeWindow) return;
                                    const handlers = [];
                                    function postMessage(data) {
                                        try {
                                            window.AlifeAndroidHost.postMessage(
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
                                })();
                                """;

    readonly Alife.Client.AlifeWindowOptions options;
    readonly string windowId;
    readonly bool crossApplication;
    readonly float density;
    readonly IWindowManager manager;
    readonly global::Android.Webkit.WebView view;
    Dialog? dialog;
    WindowManagerLayoutParams? windowLayout;
    bool visible;
    bool disposed;
    int x;
    int y;
    int width;
    int height;
}
