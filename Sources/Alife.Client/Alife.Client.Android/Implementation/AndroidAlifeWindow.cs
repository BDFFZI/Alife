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
        inputRegion = new AndroidInputRegion(view);
        view.SetBackgroundColor(options.Transparent ? Color.Transparent : Color.White);
        view.Settings.JavaScriptEnabled = true;
        view.Settings.DomStorageEnabled = true;
        view.Settings.AllowContentAccess = false;
        view.Settings.MediaPlaybackRequiresUserGesture = false;
        view.Settings.UseWideViewPort = false;
        view.Settings.LoadWithOverviewMode = false;
        view.Settings.SetSupportZoom(false);
        view.Settings.TextZoom = 100;
        AndroidDisplay.ConfigureWebView(view);
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
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutInScreen |
            WindowManagerFlags.LayoutNoLimits | WindowManagerFlags.WatchOutsideTouch,
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
    public Task SetMouseInputRegionsAsync(Rectangle[]? regions)
    {
        var snapshot = regions?.ToArray();
        return RunOnUi(() => SetMouseInputRegions(snapshot));
    }
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
        ApplyMouseInputRegions();
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
            x = location[0] / dpi, y = location[1] / dpi, width = width / dpi, height = height / dpi, density = dpi,
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

    void SetMouseInputRegions(Rectangle[]? regions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        mouseInputRegions = regions;
        bool changed = ignoreInput != (regions != null);
        ignoreInput = regions != null;
        ApplyMouseInputRegions();
        if (changed) UpdateOverlayOpacity();
    }

    void ApplyMouseInputRegions()
    {
        float dpi = AndroidDisplay.Density;
        inputRegion.Set(ignoreInput, mouseInputRegions?.Select(r => new float[] {
            r.Left * dpi, r.Top * dpi, r.Right * dpi, r.Bottom * dpi,
        }).ToArray() ?? []);
    }

    static void UpdateOverlayOpacity()
    {
        // Android 12+ combines obscuring opacity across overlays from the same UID.
        float alpha = (float)(1 - Math.Pow(0.21, 1.0 / Math.Max(1, visibleWindows.Count(w => w.ignoreInput))));
        foreach (var window in visibleWindows) {
            window.layout.Alpha = window.ignoreInput ? alpha : 1f;
            window.manager.UpdateViewLayout(window.view, window.layout);
        }
    }

    void SetKeyboardFocus(bool focus)
    {
        if (disposed || keyboardFocus == focus) return;
        keyboardFocus = focus;
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

    void BlurEditor()
    {
        if (disposed) return;
        view.EvaluateJavascript("document.activeElement?.blur();", null);
        SetKeyboardFocus(false);
    }

    internal static void UpdatePointer(System.Drawing.Point position, bool pressed)
    {
        foreach (var window in visibleWindows) {
            int[] origin = new int[2];
            window.view.GetLocationOnScreen(origin);
            float x = position.X - origin[0], y = position.Y - origin[1];
            window.view.UpdateHover(x, y);

        }
    }

    void DisposeSync()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        documentScript?.Remove();
        inputRegion.Detach();
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
        bool touching;
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e?.ActionMasked == MotionEventActions.Outside) {
                // InputDispatcher sends this for a target below this overlay, not for the
                // IME above it. Relinquish native focus without guessing global coordinates.
                owner.BlurEditor();
                return true;
            }
            if (e?.ActionMasked == MotionEventActions.Down) touching = true;
            bool handled = base.OnTouchEvent(e);
            if (e?.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel) touching = false;
            if (e != null) {
                var position = new System.Drawing.Point((int)Math.Round(e.RawX), (int)Math.Round(e.RawY));
                bool pressed = e.ActionMasked == MotionEventActions.Down;
                Post(() => { if (!owner.disposed) AndroidAlifeClient.ObservePointer(position, pressed); });
            }
            return handled; // Preserve real pointer, click, selection, focus, IME and capture.
        }

        public override bool OnHoverEvent(MotionEvent? e)
        {
            bool handled = base.OnHoverEvent(e);
            if (e != null) AndroidAlifeClient.LastCursorPosition =
                new((int)Math.Round(e.RawX), (int)Math.Round(e.RawY));
            return handled;
        }

        internal void UpdateHover(float x, float y)
        {
            if (touching) return;
            bool inside = x >= 0 && y >= 0 && x < Width && y < Height;
            using var pointer = new MotionEvent.PointerProperties { Id = 0, ToolType = MotionEventToolType.Mouse };
            using var coords = new MotionEvent.PointerCoords { X = inside ? x : -1, Y = inside ? y : -1 };
            using var motion = MotionEvent.Obtain(0, global::Android.OS.SystemClock.UptimeMillis(),
                MotionEventActions.HoverMove, 1, [pointer], [coords], 0, 0, 1, 1, 0, 0, InputSourceType.Mouse, 0)!;
            base.OnHoverEvent(motion);
            if (!inside) { motion.Action = MotionEventActions.HoverExit; base.OnHoverEvent(motion); }
        }

        public override void OnWindowFocusChanged(bool hasWindowFocus)
        {
            base.OnWindowFocusChanged(hasWindowFocus);
            if (!hasWindowFocus && owner.keyboardFocus) owner.BlurEditor();
            if (hasWindowFocus && owner.keyboardFocus) Post(() => {
                if (!owner.disposed && owner.keyboardFocus)
                    Context!.GetSystemService(Context.InputMethodService)!.JavaCast<InputMethodManager>()?
                        .ShowSoftInput(this, ShowFlags.Implicit);
            });
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
            const scale = geometry.density / window.devicePixelRatio;
            const viewportContent = `width=device-width, initial-scale=${scale}, minimum-scale=${scale}, maximum-scale=${scale}`;
            function setViewport() {
                if (!document.head) return;
                let viewport = document.querySelector('meta[name="viewport"]');
                if (!viewport) {
                    viewport = document.createElement('meta');
                    viewport.name = 'viewport';
                    document.head.appendChild(viewport);
                }
                if (viewport.content !== viewportContent) viewport.content = viewportContent;
            }
            const observer = new MutationObserver(setViewport);
            observer.observe(document, { childList: true, subtree: true });
            document.addEventListener('DOMContentLoaded', () => { setViewport(); observer.disconnect(); });
            setViewport();
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
            // Chromium screen coordinates retain system DIPs when the viewport is zoomed.
            // Convert the event's own snapshot, avoiding races with window movement.
            for (const name of ['screenX', 'screenY']) {
                const getter = Object.getOwnPropertyDescriptor(MouseEvent.prototype, name).get;
                Object.defineProperty(MouseEvent.prototype, name, { configurable: true,
                    get() { return getter.call(this) / scale; } });
            }
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
    readonly AndroidInputRegion inputRegion;
    static readonly HashSet<AndroidAlifeWindow> visibleWindows = [];
    readonly WindowManagerLayoutParams layout;
    readonly IScriptHandler? documentScript;
    Rectangle bounds;
    Rectangle publishedBounds;
    string geometryJson = "{}";
    bool visible;
    bool navigated;
    bool disposed;
    bool ignoreInput;
    bool keyboardFocus;
    Rectangle[]? mouseInputRegions;
}

