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
        HideSystemBars();
        RequestNotificationPermission();
    }

    protected override void OnResume()
    {
        base.OnResume();
        HideSystemBars();
        try
        {
            StartForegroundService(new Intent(this, typeof(KeepAliveService)));
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("Alife", "启动保活服务失败：" + e.Message);
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            HideSystemBars();
    }

    void HideSystemBars()
    {
        global::Android.Views.Window? window = Window;
        if (window == null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, false);
            IWindowInsetsController? controller = window.InsetsController;
            if (controller != null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
            return;
        }

#pragma warning disable CS0618
        window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.ImmersiveSticky);
#pragma warning restore CS0618
    }

    void RequestNotificationPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) == false)
            return;
        if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Permission.Granted)
            return;
        RequestPermissions(["android.permission.POST_NOTIFICATIONS"], 0);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidActivityResultBroker.Complete(requestCode, resultCode, data);
    }
}