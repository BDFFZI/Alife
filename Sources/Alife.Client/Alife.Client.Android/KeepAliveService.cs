using Android.App;
using Android.Content;
using Android.OS;

namespace Alife.Client.Android;

/// <summary>
/// 前台服务保活：让系统不轻易回收 Alife 进程（桌宠悬浮窗依赖进程存活）。
/// 实际业务全部在主进程内，此服务只承担前台优先级。
/// </summary>
[Service(Name = "com.alife.client.KeepAliveService", Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public sealed class KeepAliveService : Service
{
    internal const string ChannelId = "alife-backend";
    internal const int NotificationId = 1;

    public override void OnCreate()
    {
        base.OnCreate();
        NotificationManager notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Alife", NotificationImportance.Low));
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Notification notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("Alife")
            .SetContentText("Alife is running")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentIntent(PendingIntent.GetActivity(this, 0,
                new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable))
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .Build();
        StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        AndroidPointerMonitor.Stop();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}
