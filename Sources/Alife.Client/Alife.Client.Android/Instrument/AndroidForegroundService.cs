using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace Alife.Client.Android;

/// <summary>宿主提供的通用前台服务入口，插件通过它获得麦克风/MediaProjection 所需的前台状态。</summary>
static class AndroidForegroundService
{
    const string RequestIdKey = "alife.requestId";
    const string TitleKey = "alife.title";
    const string TypesKey = "alife.types";

    public static async Task<Lease> AcquireAsync(
        string title, bool microphone, bool mediaProjection, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int requestId = Interlocked.Increment(ref nextRequestId);
        TaskCompletionSource<AndroidForegroundServiceHost> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
            pendingStarts.Add(requestId, completion);

        try
        {
            ForegroundService types = 0;
            if (microphone && OperatingSystem.IsAndroidVersionAtLeast(30))
                types |= ForegroundService.TypeMicrophone;
            if (mediaProjection && OperatingSystem.IsAndroidVersionAtLeast(29))
                types |= ForegroundService.TypeMediaProjection;

            await MainThread.InvokeOnMainThreadAsync(() => {
                Intent intent = new(global::Android.App.Application.Context, typeof(AndroidForegroundServiceHost));
                intent.PutExtra(RequestIdKey, requestId);
                intent.PutExtra(TitleKey, title);
                intent.PutExtra(TypesKey, (int)types);
                global::Android.App.Application.Context.StartForegroundService(intent);
            });

            AndroidForegroundServiceHost service = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            lock (gate)
                activeRequests.Add(requestId);
            return new Lease(requestId);
        }
        finally
        {
            lock (gate)
                pendingStarts.Remove(requestId);
        }
    }

    internal static void Ready(int requestId, AndroidForegroundServiceHost service)
    {
        TaskCompletionSource<AndroidForegroundServiceHost>? completion;
        lock (gate)
        {
            instance = service;
            if (pendingStarts.Remove(requestId, out completion) == false)
                return;
        }
        completion.TrySetResult(service);
    }

    internal static void Release(int requestId)
    {
        bool idle;
        AndroidForegroundServiceHost? service;
        lock (gate)
        {
            activeRequests.Remove(requestId);
            idle = activeRequests.Count == 0;
            service = instance;
        }
        if (idle)
            service?.StopSelf();
    }

    internal static void Destroyed(AndroidForegroundServiceHost service)
    {
        lock (gate)
        {
            if (ReferenceEquals(instance, service))
                instance = null;
        }
    }

    internal sealed class Lease : IDisposable
    {
        internal Lease(int requestId)
        {
            this.requestId = requestId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            Release(requestId);
        }

        readonly int requestId;
        int disposed;
    }

    static readonly object gate = new();
    static readonly Dictionary<int, TaskCompletionSource<AndroidForegroundServiceHost>> pendingStarts = [];
    static readonly HashSet<int> activeRequests = [];
    static AndroidForegroundServiceHost? instance;
    static int nextRequestId;
}

[Service(Name = "com.alife.client.AndroidForegroundService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeMicrophone | ForegroundService.TypeMediaProjection)]
public sealed class AndroidForegroundServiceHost : Service
{
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        int requestId = intent?.GetIntExtra("alife.requestId", 0) ?? 0;
        string title = intent?.GetStringExtra("alife.title") ?? "Alife";
        int types = intent?.GetIntExtra("alife.types", 0) ?? 0;
        RefreshNotification(title, (ForegroundService)types);
        AndroidForegroundService.Ready(requestId, this);
        return StartCommandResult.NotSticky;
    }

    internal void RefreshNotification(string title, ForegroundService types)
    {
        NotificationManager notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel("alife-foreground", "Alife 前台服务", NotificationImportance.Low));
        Notification notification = new Notification.Builder(this, "alife-foreground")
            .SetContentTitle(title)
            .SetContentText("插件正在使用前台能力")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .SetContentIntent(PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable))
            .Build();
        if (types != 0 && OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(2, notification, types);
        else
            StartForeground(2, notification);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        AndroidForegroundService.Destroyed(this);
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}
