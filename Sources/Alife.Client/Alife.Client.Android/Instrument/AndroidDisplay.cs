using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace Alife.Client.Android;

/// <summary>Read display metrics afresh: cached Activity metrics go stale on rotation/density changes.</summary>
internal static class AndroidDisplay
{
    // Render the application at half the system density, using the same rounded
    // density for both WebView CSS pixels and native window/cursor coordinates.
    static int WindowDensityDpi => (int)Math.Round(
        (int)global::Android.Content.Res.Resources.System!.DisplayMetrics!.DensityDpi * 0.5);

    public static float Density => WindowDensityDpi / 160f;

    public static void ConfigureWebView(global::Android.Webkit.WebView view)
    {
        view.Settings.UseWideViewPort = true;
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
        var manager = MainApplication.Instance!.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>()!;
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
            var bounds = manager.MaximumWindowMetrics!.Bounds;
            return new(bounds.Width(), bounds.Height());
        }
        DisplayMetrics metrics = new();
        manager.DefaultDisplay!.GetRealMetrics(metrics);
        return new(metrics.WidthPixels, metrics.HeightPixels);
    }
}
