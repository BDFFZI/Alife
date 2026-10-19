using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Android.Webkit;
using AndroidX.WebKit;
using System.Text.Json;
using Rectangle = System.Drawing.Rectangle;
using Color = Android.Graphics.Color;

namespace Alife.Client.Android;

/// <summary>
/// Each plugin window owns an application-overlay WebView, independent of the Activity.
/// Native screen/cursor values are physical pixels; Bounds and browser geometry are CSS/DIP pixels.
/// </summary>
sealed class AndroidAlifeWindow : IAlifeWindow
{
    public AndroidAlifeWindow(AlifeWindowOptions options, string windowId)
    {
        this.options = options;
        WindowId = windowId;
        Context context = MainApplication.Instance!;
        manager = context.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>()!;
        view = new OverlayWebView(context, this);
        view.SetBackgroundColor(options.Transparent ? Color.Transparent : Color.White);
        view.Settings.JavaScriptEnabled = true;
        view.Settings.DomStorageEnabled = true;
        view.Settings.AllowContentAccess = false;
        view.Settings.MediaPlaybackRequiresUserGesture = false;
        view.Settings.UseWideViewPort = false;
        view.Settings.LoadWithOverviewMode = false;
        view.Settings.SetSupportZoom(false);
        view.Settings.TextZoom = 100;
        view.AddJavascriptInterface(new JsBridge(this), BridgeName);
        view.SetWebViewClient(new WindowClient(this));
#if DEBUG
        global::Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
#endif
        // Modern WebViews inject before any page script. Older WebViews (including v97)
        // use deterministic HTML interception for local plugin documents instead.
        if (WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
            documentScript = WebViewCompat.AddDocumentStartJavaScript(view, BridgeScript, new HashSet<string> { "*" });

        layout = new WindowManagerLayoutParams(1, 1, WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutInScreen,
            Format.Translucent) {
            Gravity = GravityFlags.Top | GravityFlags.Left,
            SoftInputMode = SoftInput.AdjustNothing,
            Title = $"Alife: {options.Title} ({windowId})",
        };
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            layout.FitInsetsTypes = 0;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            layout.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        SetBounds(options.Bounds ?? DefaultBounds());
    }

    public event Action<string>? MessageReceived;
    public event Action<Rectangle>? BoundsChanged;
    public event Action? Disposed;
    public string WindowId { get; }
    public Task Launch() => RunOnUi(Show);
    public Task ShowAsync() => RunOnUi(Show);
    public Task HideAsync() => RunOnUi(Hide);
    public Task SetBoundsAsync(Rectangle bounds) => RunOnUi(() => SetBounds(bounds));
    public Task SetTopMostAsync(bool topMost) => RunOnUi(() => {
        ObjectDisposedException.ThrowIf(disposed, this);
        // ApplicationOverlay already stays above activities (including after Home).
        // There is no public Android API to reorder it like Win32 HWND_TOPMOST. In particular,
        // never detach/re-add every 500ms: that resets pointer capture, focus and rendering.
        if (visible) manager.UpdateViewLayout(view, layout);
    });
    public Task SetIgnoreMouseEventsAsync(bool ignore) => RunOnUi(() => SetIgnoreInput(ignore));
    public Task PostMessageAsync(string messageJson) => RunOnUi(() => {
        ObjectDisposedException.ThrowIf(disposed, this);
        view.EvaluateJavascript("window.alifeWindow?._dispatch(" + messageJson + ");", null);
    });
    public Task<string?> ExecuteScriptAsync(string script) => AndroidWindowManager.RunOnUi(() => {
        ObjectDisposedException.ThrowIf(disposed, this);
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        view.EvaluateJavascript(script, new ScriptCallback(completion));
        return completion.Task;
    }).Unwrap();
    public ValueTask DisposeAsync() => new(RunOnUi(DisposeSync));

    static Task RunOnUi(Action action) => AndroidWindowManager.RunOnUi(() => { action(); return true; });

    Rectangle DefaultBounds()
    {
        var screen = AndroidDisplay.GetSize();
        float dpi = AndroidDisplay.Density;
        int width = Math.Min(480, (int)(screen.Width / dpi));
        int height = Math.Min(640, (int)(screen.Height / dpi));
        return new Rectangle(((int)(screen.Width / dpi) - width) / 2,
            ((int)(screen.Height / dpi) - height) / 2, width, height);
    }

    void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (visible) return;
        if (!Settings.CanDrawOverlays(view.Context)) {
            Intent request = new(Settings.ActionManageOverlayPermission,
                global::Android.Net.Uri.Parse($"package:{view.Context!.PackageName}"));
            request.AddFlags(ActivityFlags.NewTask);
            view.Context.StartActivity(request);
            throw new UnauthorizedAccessException("请授予 Alife 悬浮窗权限后重试。");
        }
        SetBounds(bounds);
        manager.AddView(view, layout);
        visible = true;
        visibleWindows.Add(this);
        UpdateOverlayOpacity();
        PublishGeometry();
        // Navigation starts only after Launch: callers can subscribe to MessageReceived first.
        if (!navigated) {
            try {
                Navigate(options.Url);
                navigated = true;
            } catch {
                Hide();
                throw;
            }
        }
    }

    void Hide()
    {
        if (!visible) return;
        manager.RemoveView(view);
        visible = false;
        visibleWindows.Remove(this);
        UpdateOverlayOpacity();
    }

    void SetBounds(Rectangle requested)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var screen = AndroidDisplay.GetSize();
        float dpi = AndroidDisplay.Density;
        int width = Math.Clamp((int)Math.Round(Math.Max(1, requested.Width) * dpi), 1, screen.Width);
        int height = Math.Clamp((int)Math.Round(Math.Max(1, requested.Height) * dpi), 1, screen.Height);
        // Keep one quarter visible so a window can be moved mostly off-screen and
        // still be recovered by dragging its visible edge.
        double minX = -width * 0.75;
        double maxX = screen.Width - width * 0.25;
        double minY = -height * 0.75;
        double maxY = screen.Height - height * 0.25;
        int x = (int)Math.Clamp(Math.Round(requested.X * dpi), minX, maxX);
        int y = (int)Math.Clamp(Math.Round(requested.Y * dpi), minY, maxY);
        layout.Width = width;
        layout.Height = height;
        layout.X = x;
        layout.Y = y;
        bounds = new Rectangle((int)Math.Round(x / dpi), (int)Math.Round(y / dpi),
            (int)Math.Round(width / dpi), (int)Math.Round(height / dpi));
        if (visible) manager.UpdateViewLayout(view, layout);
        PublishGeometry();
    }

    internal void OnLayout()
    {
        if (disposed || !visible) return;
        PublishGeometry();
    }

    internal void OnConfigurationChanged()
    {
        if (!disposed) SetBounds(bounds);
    }

    void PublishGeometry()
    {
        float dpi = AndroidDisplay.Density;
        int[] location = [layout.X, layout.Y];
        if (visible && view.IsLaidOut) view.GetLocationOnScreen(location);
        int width = visible && view.Width > 0 ? view.Width : layout.Width;
        int height = visible && view.Height > 0 ? view.Height : layout.Height;
        var screen = AndroidDisplay.GetSize();
        var actual = new Rectangle((int)Math.Round(location[0] / dpi), (int)Math.Round(location[1] / dpi),
            (int)Math.Round(width / dpi), (int)Math.Round(height / dpi));
        // getMetrics is called on WebView's Java bridge thread; never access View APIs there.
        string json = JsonSerializer.Serialize(new {
            x = location[0] / dpi, y = location[1] / dpi, width = width / dpi, height = height / dpi,
            screenWidth = screen.Width / dpi, screenHeight = screen.Height / dpi,
        });
        Volatile.Write(ref geometryJson, json);
        if (visible && navigated)
            view.EvaluateJavascript("window.alifeWindow?._setGeometry(" + json + ");", null);
        if (actual != publishedBounds) {
            publishedBounds = actual;
            BoundsChanged?.Invoke(actual);
        }
    }

    void SetIgnoreInput(bool ignore)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ignoreInput == ignore) return;
        ignoreInput = ignore;
        SetKeyboardFocus(false);
        layout.Flags = ignore ? layout.Flags | WindowManagerFlags.NotTouchable
            : layout.Flags & ~WindowManagerFlags.NotTouchable;
        // Android 12+ blocks touches through untrusted overlays above the obscuring-opacity limit.
        UpdateOverlayOpacity();
        if (visible) manager.UpdateViewLayout(view, layout);
    }

    static void UpdateOverlayOpacity()
    {
        // Android combines obscuring opacity across overlays from the same UID.
        // Budget across ignored windows so overlapping windows still permit passthrough.
        float alpha = (float)(1 - Math.Pow(0.21, 1.0 / Math.Max(1, visibleWindows.Count(w => w.ignoreInput))));
        foreach (var window in visibleWindows) {
            window.layout.Alpha = window.ignoreInput ? alpha : 1f;
            window.manager.UpdateViewLayout(window.view, window.layout);
        }
    }

    void SetKeyboardFocus(bool focus)
    {
        if (disposed || (focus && ignoreInput)) return;
        layout.Flags = focus ? layout.Flags & ~WindowManagerFlags.NotFocusable
            : layout.Flags | WindowManagerFlags.NotFocusable;
        if (visible) manager.UpdateViewLayout(view, layout);
        var keyboard = view.Context!.GetSystemService(Context.InputMethodService)!.JavaCast<InputMethodManager>();
        if (focus) {
            view.RequestFocus();
            keyboard?.ShowSoftInput(view, ShowFlags.Implicit);
        } else {
            keyboard?.HideSoftInputFromWindow(view.WindowToken, HideSoftInputFlags.None);
        }
    }

    void DisposeSync()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        documentScript?.Remove();
        view.RemoveJavascriptInterface(BridgeName);
        view.StopLoading();
        view.Destroy();
        view.Dispose();
        Disposed?.Invoke();
    }

    void Navigate(string url)
    {
        if (System.IO.Path.IsPathRooted(url)) url = new Uri(System.IO.Path.GetFullPath(url)).AbsoluteUri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? target) ||
            (target.Scheme is not ("http" or "https" or "file") && url != "about:blank"))
            throw new NotSupportedException("Web windows require an absolute HTTP, HTTPS or file URL, or about:blank.");
        if (target.IsFile && !File.Exists(target.LocalPath))
            throw new FileNotFoundException("Window HTML file not found.", target.LocalPath);
        view.Settings.AllowFileAccess = target.IsFile;
        view.Settings.AllowFileAccessFromFileURLs = target.IsFile;
        if (documentScript == null && target.Scheme is "http" or "https")
            throw new NotSupportedException("此版本 Android WebView 仅支持本地 HTML 窗口；网络窗口需要更新 Android System WebView。");
        if (documentScript == null && url == "about:blank")
            view.LoadDataWithBaseURL(null, "<script>" + BridgeScript + "</script>", "text/html", "utf-8", null);
        else
            view.LoadUrl(target.AbsoluteUri);
    }

    sealed class OverlayWebView(Context context, AndroidAlifeWindow owner) : global::Android.Webkit.WebView(context)
    {
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e != null) {
                // RawX/Y include the window origin; GetX/Y are viewport-local physical pixels.
                AndroidAlifeClient.LastCursorPosition = new System.Drawing.Point((int)Math.Round(e.RawX), (int)Math.Round(e.RawY));
            }
            return base.OnTouchEvent(e); // Preserve real pointer, click, selection, focus, IME and capture.
        }
        public override bool OnGenericMotionEvent(MotionEvent? e)
        {
            if (e != null)
                AndroidAlifeClient.LastCursorPosition = new System.Drawing.Point((int)Math.Round(e.RawX), (int)Math.Round(e.RawY));
            return base.OnGenericMotionEvent(e);
        }
        protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
        {
            base.OnLayout(changed, left, top, right, bottom);
            owner.OnLayout();
        }
        protected override void OnConfigurationChanged(global::Android.Content.Res.Configuration? config)
        {
            base.OnConfigurationChanged(config);
            Post(owner.OnConfigurationChanged);
        }
    }

    sealed class ScriptCallback(TaskCompletionSource<string?> completion) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => completion.TrySetResult(value?.ToString());
    }

    sealed class JsBridge(AndroidAlifeWindow window) : Java.Lang.Object
    {
        [JavascriptInterface, Java.Interop.Export("postMessage")]
        public void PostMessage(string message) => _ = RunOnUi(() => {
            if (!window.disposed) window.MessageReceived?.Invoke(message);
        });
        [JavascriptInterface, Java.Interop.Export("getMetrics")]
        public string GetMetrics() => Volatile.Read(ref window.geometryJson);
        [JavascriptInterface, Java.Interop.Export("setKeyboardFocus")]
        public void SetKeyboardFocus(bool focus) => _ = RunOnUi(() => window.SetKeyboardFocus(focus));
    }

    sealed class WindowClient(AndroidAlifeWindow owner) : WebViewClient
    {
        public override WebResourceResponse? ShouldInterceptRequest(global::Android.Webkit.WebView? webView, IWebResourceRequest? request)
        {
            if (owner.documentScript == null && request?.IsForMainFrame == true &&
                Uri.TryCreate(request.Url?.ToString(), UriKind.Absolute, out var target) && target.IsFile) {
                string html = File.ReadAllText(target.LocalPath);
                string script = "<script>" + BridgeScript + "</script>";
                var doctype = System.Text.RegularExpressions.Regex.Match(html, @"^\s*<!doctype[^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                html = html.Insert(doctype.Success ? doctype.Length : 0, script);
                return new WebResourceResponse("text/html", "utf-8",
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)));
            }
            return base.ShouldInterceptRequest(webView, request);
        }
        public override void OnPageFinished(global::Android.Webkit.WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            owner.PublishGeometry();
        }
    }

    const string BridgeName = "AlifeAndroidHost";
    const string BridgeScript = """
        (() => {
            if (window !== window.top || window.alifeWindow) return;
            const host = window.AlifeAndroidHost;
            const handlers = [];
            let geometry = JSON.parse(host.getMetrics());
            const define = (obj, name, getter) => Object.defineProperty(obj, name, { configurable: true, get: getter });
            for (const name of ['screenX', 'screenLeft']) define(window, name, () => geometry.x);
            for (const name of ['screenY', 'screenTop']) define(window, name, () => geometry.y);
            define(window, 'outerWidth', () => geometry.width);
            define(window, 'outerHeight', () => geometry.height);
            for (const name of ['width', 'availWidth']) define(screen, name, () => geometry.screenWidth);
            for (const name of ['height', 'availHeight']) define(screen, name, () => geometry.screenHeight);
            define(screen, 'availLeft', () => 0);
            define(screen, 'availTop', () => 0);
            window.alifeWindow = {
                postMessage(data) { host.postMessage(typeof data === 'string' ? data : JSON.stringify(data)); },
                onMessage(handler) { handlers.push(handler); },
                _dispatch(msg) { for (const handler of handlers) { try { handler(msg); } catch (e) { console.error(e); } } },
                _setGeometry(value) { geometry = value; },
            };
            // Preserve native PointerEvent/MouseEvent screen coordinates. They already include
            // the overlay origin at event creation; recomputing them here races with window moves.
            document.addEventListener('focusin', event => {
                if (event.target.matches('input,textarea,[contenteditable]')) host.setKeyboardFocus(true);
            });
            document.addEventListener('focusout', () => {
                setTimeout(() => {
                    if (!document.activeElement?.matches('input,textarea,[contenteditable]')) host.setKeyboardFocus(false);
                }, 0);
            });
        })();
        """;

    readonly AlifeWindowOptions options;
    readonly IWindowManager manager;
    readonly OverlayWebView view;
    readonly WindowManagerLayoutParams layout;
    readonly IScriptHandler? documentScript;
    Rectangle bounds;
    Rectangle publishedBounds;
    string geometryJson = "{}";
    bool visible;
    bool navigated;
    bool disposed;
    bool ignoreInput;
    static readonly HashSet<AndroidAlifeWindow> visibleWindows = [];
}

