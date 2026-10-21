using Android.Content;
using Android.Runtime;
using Android.Views;
using System.Text.RegularExpressions;

namespace Alife.Client.Android;

/// <summary>
/// Shizuku's shell identity can observe touchscreen evdev events even when all our windows
/// are NotTouchable. Never grabs the device or consumes touches delivered to other apps.
/// Shizuku is a required Android runtime dependency.
/// </summary>
static class AndroidPointerMonitor
{
    public static void EnsureStarted()
    {
        lock (gate) {
            if (running || Environment.TickCount64 - lastAttempt < 10000 || !ShizukuBridge.HasPermission) return;
            running = true;
            lastAttempt = Environment.TickCount64;
            cancellation = new CancellationTokenSource();
            _ = Task.Run(() => MonitorAsync(cancellation.Token));
        }
    }

    public static void Stop()
    {
        lock (gate) { cancellation?.Cancel(); }
    }

    static async Task MonitorAsync(CancellationToken token)
    {
        try {
            var devices = await ShizukuBridge.ExecuteAsync("getevent -lp", token);
            foreach (Match device in Regex.Matches(devices.Output,
                @"add device \d+: (?<path>/dev/input/event\d+)(?<details>.*?)(?=add device|\z)", RegexOptions.Singleline)) {
                string details = device.Groups["details"].Value;
                if (!details.Contains("INPUT_PROP_DIRECT")) continue;
                Match xRange = Regex.Match(details, @"ABS_MT_POSITION_X\s+:.*?min (\d+), max (\d+)");
                Match yRange = Regex.Match(details, @"ABS_MT_POSITION_Y\s+:.*?min (\d+), max (\d+)");
                if (!xRange.Success || !yRange.Success) continue;
                var decoder = new AndroidTouchDecoder(int.Parse(xRange.Groups[1].Value), int.Parse(xRange.Groups[2].Value),
                    int.Parse(yRange.Groups[1].Value), int.Parse(yRange.Groups[2].Value));
                string path = device.Groups["path"].Value; // Only numeric evdev paths from the regex.
                await ShizukuBridge.ReadLinesAsync("getevent -lt " + path, line => {
                    if (!decoder.Read(line, out double x, out double y)) return;
                    bool pressed = decoder.IsContactStart;
                    // Read display rotation/size on the UI thread and keep the public cursor in physical pixels.
                    _ = AndroidWindowManager.RunOnUi(() => {
                        var manager = MainApplication.Instance!.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
                        (x, y) = manager.DefaultDisplay!.Rotation switch {
                            SurfaceOrientation.Rotation90 => (y, 1 - x),
                            SurfaceOrientation.Rotation180 => (1 - x, 1 - y),
                            SurfaceOrientation.Rotation270 => (1 - y, x),
                            _ => (x, y),
                        };
                        var size = AndroidDisplay.GetSize();
                        AndroidAlifeClient.ObservePointer(new((int)Math.Round(x * (size.Width - 1)),
                            (int)Math.Round(y * (size.Height - 1))), pressed);
                        return true;
                    });
                }, token);
                return;
            }
            global::Android.Util.Log.Warn("Alife", "Shizuku 未找到可读取的触摸屏。");
        } catch (OperationCanceledException) {
        } catch (Exception e) {
            global::Android.Util.Log.Warn("Alife", "全局触摸监视不可用：" + e.Message);
        } finally {
            lock (gate) { running = false; cancellation?.Dispose(); cancellation = null; }
        }
    }

    static readonly object gate = new();
    static bool running;
    static long lastAttempt = -10000;
    static CancellationTokenSource? cancellation;
}
