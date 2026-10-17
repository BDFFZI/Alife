using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Alife.Foundation;
using Alife.Client.Android.Backend;
using Android.App;
using Android.Content;
using Android.OS;
using Process = System.Diagnostics.Process;
using Environment = System.Environment;
using OperationCanceledException = System.OperationCanceledException;

namespace Alife.Client.Android;

[Service(Name = "com.alife.client.BackendService", Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public sealed class BackendService : Service
{
    internal static BackendService? Current { get; private set; }
    internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Uri? Address { get; private set; }
    internal string Token { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    internal event Action<string>? ProgressChanged;
    readonly CancellationTokenSource lifetime = new();
    readonly Dictionary<string, LauncherWebWindow> windows = new();
    readonly Handler ui = new(Looper.MainLooper!);
    Process? process;
    PowerManager.WakeLock? wakeLock;
    string logPath = "";
    readonly object logGate = new();

    public override void OnCreate()
    {
        base.OnCreate();
        Current = this;
        const string channel = "alife-backend";
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(channel, "Alife", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(this, channel).SetContentTitle("Alife")
            .SetContentText("Alife is running").SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentIntent(open).SetOngoing(true).Build();
        StartForeground(1, notification);
        var power = (PowerManager)GetSystemService(PowerService)!;
        wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Alife:Backend");
        wakeLock!.Acquire();
        _ = Task.Run(RunAsync);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId) => StartCommandResult.NotSticky;
    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        StopSelf();
        base.OnTaskRemoved(rootIntent);
    }

    async Task RunAsync()
    {
        try
        {
            var installation = new DotNetInstallation();
            WriteLog($"Runtime directory: {AlifePath.RuntimeFolderPath}");
            await installation.EnsureAsync(message => { WriteLog(message); ProgressChanged?.Invoke(message); }, lifetime.Token);
            string backend = Path.Combine(AlifePath.RuntimeFolderPath, "ClientServer");
            Directory.CreateDirectory(backend);
            using (var archive = typeof(BackendService).Assembly.GetManifestResourceStream("Backend")!)
            using (ZipArchive zip = new(archive)) zip.ExtractToDirectory(backend, true);
            string launcher = Path.Combine(AlifePath.RuntimeFolderPath, "dotnet-launcher");
            using (var source = typeof(BackendService).Assembly.GetManifestResourceStream("SdkLauncher")!)
            using (var destination = File.Create(launcher)) source.CopyTo(destination);
            using (TcpListener socket = new(IPAddress.Loopback, 0))
            {
                socket.Start();
                Address = new Uri($"http://127.0.0.1:{((IPEndPoint)socket.LocalEndpoint).Port}/");
            }
            logPath = Path.Combine(AlifePath.RuntimeFolderPath, "backend.log");
            ProcessStartInfo start = new("/system/bin/linker64") {
                WorkingDirectory = backend, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add(launcher);
            start.ArgumentList.Add(Path.Combine(backend, "Alife.Client.Android.Backend.dll"));
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add(Address.ToString());
            start.Environment["DOTNET_ROOT"] = installation.SdkPath;
            ApplySdkEnvironment(start, installation, launcher);
            start.Environment["ALIFE_HOST_TOKEN"] = Token;
            start.Environment["ALIFE_RUNTIME_PATH"] = AlifePath.RuntimeFolderPath;
            start.Environment["ALIFE_STORAGE_PATH"] = AlifePath.StorageFolderPath;
            File.WriteAllText(logPath, "");
            process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Alife backend.");
            process.OutputDataReceived += (_, args) => WriteLog(args.Data);
            process.ErrorDataReceived += (_, args) => WriteLog(args.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using HttpClient http = new(new SocketsHttpHandler()) { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("X-Alife-Host", Token);
            ProgressChanged?.Invoke("Starting backend...");
            bool ready = false;
            for (int attempt = 0; attempt < 120; attempt++)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException($"Backend exited ({process.ExitCode}).\n" + File.ReadAllText(logPath));
                try
                {
                    using var response = await http.GetAsync("host/ready", lifetime.Token);
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                }
                catch (HttpRequestException) { }
                await Task.Delay(500, lifetime.Token);
            }
            if (!ready) throw new TimeoutException("Alife backend did not become ready.");
            Ready.TrySetResult();
            while (!lifetime.IsCancellationRequested)
            {
                NativeHostCommand? command;
                try { command = await http.GetFromJsonAsync<NativeHostCommand>("host/commands", lifetime.Token); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { continue; }
                if (command is null) continue;
                string? error = null;
                try { await ExecuteOnUiAsync(command); }
                catch (Exception exception) { error = exception.Message; }
                using var response = await http.PostAsJsonAsync("host/results", new NativeHostResult(command.Id, error), lifetime.Token);
                response.EnsureSuccessStatusCode();
                if (command.Action == "exit") { ui.Post(StopSelf); break; }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Ready.TrySetException(error);
            WriteLog(error.ToString());
            ProgressChanged?.Invoke(error.Message);
            ui.Post(StopSelf);
        }
        finally
        {
            if (process is { HasExited: false }) process.Kill(true);
            process?.Dispose();
        }
    }

    static void ApplySdkEnvironment(ProcessStartInfo start, DotNetInstallation installation, string launcher)
    {
        string hostfxr = Directory.GetDirectories(Path.Combine(installation.SdkPath, "host", "fxr"))
            .OrderByDescending(path => Version.Parse(Path.GetFileName(path))).First();
        start.Environment["ALIFE_HOSTFXR"] = Path.Combine(hostfxr, "libhostfxr.so");
        start.Environment["ALIFE_DOTNET_LAUNCHER"] = launcher;
        start.Environment["LD_LIBRARY_PATH"] = installation.LibraryPath;
        start.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        start.Environment["TMPDIR"] = Path.GetTempPath();
        start.Environment["SSL_CERT_FILE"] = Path.Combine(installation.DirectoryPath, "usr", "etc", "tls", "cert.pem");
        start.Environment["MSBuildEnableWorkloadResolver"] = "false";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    }

    Task ExecuteOnUiAsync(NativeHostCommand command)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => {
            try
            {
                switch (command.Action)
                {
                    case "create": windows.Add(command.WindowId!, new LauncherWebWindow(this, command.Options!, Address!, command.Url)); break;
                    case "show": windows[command.WindowId!].Show(); break;
                    case "hide": windows[command.WindowId!].Hide(); break;
                    case "navigate": windows[command.WindowId!].Navigate(command.Url!); break;
                    case "dispose": if (windows.Remove(command.WindowId!, out var window)) window.Dispose(); break;
                    case "hide-main": MainActivity.Current?.MoveTaskToBack(true); break;
                    case "exit": MainActivity.Current?.Finish(); break;
                    default: throw new NotSupportedException(command.Action);
                }
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task.WaitAsync(lifetime.Token);
    }

    void WriteLog(string? message)
    {
        if (message is null) return;
        global::Android.Util.Log.Info("AlifeBackend", message);
        if (logPath.Length > 0) lock (logGate) File.AppendAllText(logPath, message + Environment.NewLine);
    }

    public override void OnDestroy()
    {
        lifetime.Cancel();
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        wakeLock?.Dispose();
        wakeLock = null;
        foreach (var window in windows.Values) window.Dispose();
        windows.Clear();
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }
}
