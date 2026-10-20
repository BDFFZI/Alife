using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Alife.Client.Android;

[Activity(Label = "Alife", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    Theme = "@style/Maui.SplashTheme",
    WindowSoftInputMode = SoftInput.AdjustResize,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    internal static MainActivity? Current { get; private set; }

    protected override void AttachBaseContext(Context? newBase)
        => base.AttachBaseContext(AndroidDisplay.CreateScaledContext(newBase!));

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        RequestNotificationPermission();
    }

    protected override void OnResume()
    {
        base.OnResume();
        try
        {
            StartForegroundService(new Intent(this, typeof(KeepAliveService)));
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("Alife", "启动保活服务失败：" + e.Message);
        }
    }

    void RequestNotificationPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) == false)
            return;
        if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Permission.Granted)
            return;
        RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 0);
    }
}
