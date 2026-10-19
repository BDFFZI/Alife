using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace Alife.Client.Android;

/// <summary>Read display metrics afresh: cached Activity metrics go stale on rotation/density changes.</summary>
internal static class AndroidDisplay
{
    // Map the reference device's Android density 3.0 to the requested logical
    // desktop-style scale 2.0 (the ratio also works on other density buckets).
    public static float Density => MainApplication.Instance!.Resources!.DisplayMetrics!.Density * (2f / 3f);

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
