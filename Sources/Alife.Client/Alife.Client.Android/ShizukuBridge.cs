using System.Text;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Moe.Shizuku.Server;
using Rikka.Shizuku;
using ShizukuApi = Rikka.Shizuku.Shizuku;

namespace Alife.Client.Android;

/// <summary>Shizuku shell 执行结果。</summary>
public sealed record ShizukuShellResult(int ExitCode, string Output);

/// <summary>
/// 将 Shizuku 的 shell 能力封装为客户端内部服务，供 Android 插件调用。
/// 绑定接收由 AndroidManifest.xml 中声明的 rikka.shizuku.ShizukuProvider 完成。
/// </summary>
public static class ShizukuBridge
{
    /// <summary>Shizuku 安装的 Python 运行时根目录（shell 可执行）。</summary>
    public const string PythonRoot = "/data/local/tmp/Alife/Python";
    public const string PythonUsr = PythonRoot + "/usr";

    const int PermissionRequestCode = 0x5A17;

    /// <summary>Shizuku 服务是否已连接。</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                return ShizukuApi.PingBinder();
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Shizuku 服务身份：2000 为 ADB shell，0 为 root。</summary>
    public static int Uid
    {
        get
        {
            try
            {
                return IsAvailable ? ShizukuApi.Uid : -1;
            }
            catch
            {
                return -1;
            }
        }
    }

    /// <summary>当前应用是否已获得 Shizuku 授权。</summary>
    public static bool HasPermission
    {
        get
        {
            try
            {
                return IsAvailable && ShizukuApi.CheckSelfPermission() == (int)Permission.Granted;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>等待 Shizuku 绑定到达（应用启动后 Provider 异步投递）。</summary>
    public static async Task<bool> WaitForBinderAsync(TimeSpan timeout)
    {
        if (IsAvailable)
            return true;

        TaskCompletionSource<bool> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BinderListener listener = new(() => received.TrySetResult(true));
        try
        {
            ShizukuApi.AddBinderReceivedListenerSticky(listener);
            Task completed = await Task.WhenAny(received.Task, Task.Delay(timeout));
            return completed == received.Task;
        }
        finally
        {
            ShizukuApi.RemoveBinderReceivedListener(listener);
        }
    }

    /// <summary>请求 Shizuku 授权；已授权时立即返回 true。</summary>
    public static Task<bool> RequestPermissionAsync()
    {
        if (HasPermission)
            return Task.FromResult(true);
        if (IsAvailable == false)
            return Task.FromResult(false);
        if (permissionTask != null)
            return permissionTask.Task;

        permissionTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        permissionListener = new PermissionListener();
        ShizukuApi.AddRequestPermissionResultListener(permissionListener);
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => ShizukuApi.RequestPermission(PermissionRequestCode));
        return permissionTask.Task;
    }

    /// <summary>以 Shizuku 的 shell 身份执行命令，并返回合并后的输出。</summary>
    public static async Task<ShizukuShellResult> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (IsAvailable == false)
            throw new InvalidOperationException("Shizuku 未连接。");
        if (HasPermission == false)
            throw new InvalidOperationException("Shizuku 未授权。");

        IBinder binder = ShizukuApi.Binder ?? throw new InvalidOperationException("Shizuku 绑定不可用。");
        IShizukuService service = IShizukuService.Stub.AsInterface(binder)
            ?? throw new InvalidOperationException("无法获取 Shizuku 服务。");
        IRemoteProcess process = service.NewProcess(["/system/bin/sh", "-c", command], null, null)
            ?? throw new InvalidOperationException("无法创建远程进程。");

        try
        {
            ParcelFileDescriptor? output = process.InputStream;
            if (output == null)
                return new ShizukuShellResult(-1, "");

            using ParcelFileDescriptor.AutoCloseInputStream fileInput = new(output);
            using Stream stream = new InputStreamInvoker(fileInput);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read <= 0)
                    break;
                buffer.Write(chunk, 0, read);
            }

            int exitCode = await Task.Run(process.WaitFor, cancellationToken);
            return new ShizukuShellResult(exitCode, Encoding.UTF8.GetString(buffer.ToArray()));
        }
        finally
        {
            process.Destroy();
        }
    }

    static TaskCompletionSource<bool>? permissionTask;
    static PermissionListener? permissionListener;

    sealed class PermissionListener : Java.Lang.Object, ShizukuApi.IOnRequestPermissionResultListener
    {
        public void OnRequestPermissionResult(int requestCode, int grantResult)
        {
            if (requestCode != PermissionRequestCode)
                return;
            ShizukuApi.RemoveRequestPermissionResultListener(this);
            permissionListener = null;
            TaskCompletionSource<bool>? task = permissionTask;
            permissionTask = null;
            task?.TrySetResult(grantResult == (int)Permission.Granted);
        }
    }

    sealed class BinderListener(Action onReceived) : Java.Lang.Object, ShizukuApi.IOnBinderReceivedListener
    {
        public void OnBinderReceived() => onReceived();
    }
}
