using System.Text;
using Alife.Foundation;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Moe.Shizuku.Server;

namespace Alife.Client.Android;

/// <summary>
/// 插件访问 Android 平台能力的唯一公共入口。客户端内部实现类型均为 internal。
/// </summary>
public static class AndroidAlifePlatform
{
    public static bool ShizukuAvailable => ShizukuBridge.IsAvailable;
    public static bool ShizukuGranted => ShizukuBridge.HasPermission;
    public static int ShizukuUid => ShizukuBridge.Uid;

    public static Task<bool> RequestShizukuPermissionAsync() => ShizukuBridge.RequestPermissionAsync();

    public static async Task<ShellResult> ExecuteShellAsync(
        string command, CancellationToken cancellationToken = default)
    {
        ShizukuShellResult result = await ShizukuBridge.ExecuteAsync(command, cancellationToken);
        return new ShellResult(result.ExitCode, result.Output);
    }

    /// <summary>启动一个需要 Activity 返回结果的系统界面，例如 MediaProjection 授权。</summary>
    public static Task<(global::Android.App.Result ResultCode, Intent? Data)> RequestActivityResultAsync(
        Intent intent, CancellationToken cancellationToken = default)
        => AndroidActivityResultBroker.RequestAsync(intent, cancellationToken);

    /// <summary>获取通用前台服务租约，释放返回值即停止服务。</summary>
    public static async Task<IDisposable> AcquireForegroundServiceAsync(
        string title, bool microphone, bool mediaProjection, CancellationToken cancellationToken = default)
        => await AndroidForegroundService.AcquireAsync(title, microphone, mediaProjection, cancellationToken);

    /// <summary>
    /// 启动 Shizuku Python 进程。脚本路径必须能被 shell 用户读取。
    /// </summary>
    internal static PythonProcess StartPython(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptPath);
        if (ShizukuGranted == false)
            throw new InvalidOperationException("Shizuku 未连接或未授权。");

        string root = ShizukuBridge.PythonRoot;
        string usr = ShizukuBridge.PythonUsr;
        string command = $"""
            export HOME={root} TMPDIR={root}/tmp PYTHONHOME={usr}
            export LD_LIBRARY_PATH={usr}/lib PATH={usr}/bin:$PATH
            export SSL_CERT_FILE={usr}/etc/tls/cert.pem PYTHONIOENCODING=utf-8 PYTHONUTF8=1
            {BuildMirrorEnvironmentExports()}
            mkdir -p {root}/tmp
            cd {root} || exit 1
            exec {usr}/bin/python -u {Quote(scriptPath)}
            """;
        return new PythonProcess(ShizukuBridge.StartProcess(command.Replace("\r\n", "\n")));
    }

    internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    internal static string BuildMirrorEnvironmentExports()
    {
        StringBuilder exports = new();
        foreach ((string key, string value) in AlifeMirror.MirrorEnvironmentVariables)
        {
            if (string.IsNullOrEmpty(value) || ProtectedEnvironmentKeys.Contains(key) || IsValidEnvironmentKey(key) == false)
                continue;

            if (exports.Length > 0)
                exports.Append('\n');
            exports.Append("export ").Append(key).Append('=').Append(Quote(value));
        }
        return exports.ToString();
    }

    static readonly HashSet<string> ProtectedEnvironmentKeys = new(StringComparer.OrdinalIgnoreCase) {
        "HOME", "PATH", "TMPDIR", "PYTHONHOME", "PYTHONPATH",
        "LD_LIBRARY_PATH", "LD_PRELOAD", "SSL_CERT_FILE", "SSL_CERT_DIR",
    };

    static bool IsValidEnvironmentKey(string key)
    {
        if (key.Length == 0)
            return false;
        if (char.IsAsciiLetter(key[0]) == false && key[0] != '_')
            return false;
        foreach (char character in key)
        {
            if (char.IsAsciiLetterOrDigit(character) == false && character != '_')
                return false;
        }
        return true;
    }

    public sealed record ShellResult(int ExitCode, string Output);

    internal sealed class PythonProcess : IAlifeProcess
    {
        internal PythonProcess(IRemoteProcess process)
        {
            this.process = process;
            StandardInput = new StreamWriter(new OutputStreamInvoker(
                new ParcelFileDescriptor.AutoCloseOutputStream(process.OutputStream!)), new UTF8Encoding(false)) {
                AutoFlush = true,
            };
            StandardOutput = new StreamReader(new InputStreamInvoker(
                new ParcelFileDescriptor.AutoCloseInputStream(process.InputStream!)), Encoding.UTF8);
            StandardError = new StreamReader(new InputStreamInvoker(
                new ParcelFileDescriptor.AutoCloseInputStream(process.ErrorStream!)), Encoding.UTF8);
            Completion = Task.Run(process.WaitFor);
        }

        readonly IRemoteProcess process;

        public StreamWriter StandardInput { get; }
        public StreamReader StandardOutput { get; }
        public StreamReader StandardError { get; }
        public Task<int> Completion { get; }
        public bool HasExited => Completion.IsCompleted;

        public void Kill()
        {
            if (HasExited == false)
                process.Destroy();
        }

        public void Dispose()
        {
            Kill();
            StandardInput.Dispose();
            StandardOutput.Dispose();
            StandardError.Dispose();
            process.Dispose();
        }
    }
}
