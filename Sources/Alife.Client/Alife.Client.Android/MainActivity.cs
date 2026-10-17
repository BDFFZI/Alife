using Android.App;
using Android.Content;
using Android.OS;
using Android.Webkit;
using Android.Widget;
using OperationCanceledException = System.OperationCanceledException;

namespace Alife.Client.Android;

[Activity(Label = "Alife", MainLauncher = true, Exported = true,
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation |
        global::Android.Content.PM.ConfigChanges.ScreenSize |
        global::Android.Content.PM.ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    internal static MainActivity? Current { get; private set; }
    WebView? webView;
    readonly CancellationTokenSource lifetime = new();

    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        Current = this;
        Window!.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        TextView loading = new(this) { Text = "Starting Alife..." };
        SetContentView(loading);
        try
        {
            StartForegroundService(new Intent(this, typeof(BackendService)));
            while (BackendService.Current is null) await Task.Delay(100, lifetime.Token);
            var service = BackendService.Current;
            service.ProgressChanged += UpdateProgress;
            try { await service.Ready.Task.WaitAsync(lifetime.Token); }
            finally
            {
                service.ProgressChanged -= UpdateProgress;
                Window.ClearFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
            }
            CookieManager.Instance!.SetCookie(service.Address!.ToString(), $"AlifeHost={service.Token}; Path=/; SameSite=Strict");
            CookieManager.Instance.Flush();
            webView = CreateWebView(this);
            SetContentView(webView);
            webView.LoadUrl(service.Address.ToString());
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { loading.Text = error.Message; }
        void UpdateProgress(string message) => RunOnUiThread(() => loading.Text = message);
    }

    internal static WebView CreateWebView(Context context)
    {
#if DEBUG
        WebView.SetWebContentsDebuggingEnabled(true);
#endif
        WebView view = new(context);
        view.Settings.JavaScriptEnabled = true;
        view.Settings.DomStorageEnabled = true;
        view.Settings.AllowFileAccess = false;
        view.Settings.AllowContentAccess = false;
        view.SetWebViewClient(new WebViewClient());
        return view;
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        if (ReferenceEquals(Current, this)) Current = null;
        webView?.Destroy();
        webView?.Dispose();
        base.OnDestroy();
    }
}
