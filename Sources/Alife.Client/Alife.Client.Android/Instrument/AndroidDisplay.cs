using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.WebKit;
using System.Globalization;

namespace Alife.Client.Android;

/// <summary>Read display metrics afresh: cached Activity metrics go stale on rotation/density changes.</summary>
static class AndroidDisplay
{
    static readonly List<IScriptHandler> mainScripts = [];

    // Single source of truth for the logical density. Native window/cursor coordinates and
    // WebView CSS pixels both derive from this; Chromium ignores the Android Context density,
    // so each WebView additionally applies a viewport scale (see index.html and bridge script).
    public const float UiScale = 0.5f;

    static int WindowDensityDpi => (int)Math.Round(
        (int)global::Android.Content.Res.Resources.System!.DisplayMetrics!.DensityDpi * UiScale);

    public static float Density => WindowDensityDpi / 160f;

    public static void ConfigureWebView(global::Android.Webkit.WebView view)
    {
        view.Settings.UseWideViewPort = true;
    }

    public static void ConfigureMainWebView(global::Android.Webkit.WebView view)
    {
        ConfigureWebView(view);
        if (!WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
            return;

        foreach (IScriptHandler previous in mainScripts)
            previous.Remove();
        mainScripts.Clear();

        // Runs before the host page scripts, so index.html can size its viewport synchronously.
        string density = Density.ToString(CultureInfo.InvariantCulture);
        string script = $$"""
            window.__alifeDensity = {{density}};
            """;
        IScriptHandler? handler = WebViewCompat.AddDocumentStartJavaScript(view, script, new HashSet<string> { "*" });
        if (handler != null)
            mainScripts.Add(handler);
    }

    public static Context CreateScaledContext(Context context)
    {
        var configuration = new global::Android.Content.Res.Configuration(context.Resources!.Configuration) {
            DensityDpi = WindowDensityDpi,
        };
        return context.CreateConfigurationContext(configuration)!;
    }

    public static System.Drawing.Size GetSize()
    {
        var manager = MainApplication.Instance!.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var bounds = manager.MaximumWindowMetrics.Bounds;
            return new(bounds.Width(), bounds.Height());
        }
        DisplayMetrics metrics = new();
        manager.DefaultDisplay!.GetRealMetrics(metrics);
        return new(metrics.WidthPixels, metrics.HeightPixels);
    }
}