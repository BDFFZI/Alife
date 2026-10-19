using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Alife.Client;
using System.Drawing;
using System.Text.Json;

namespace Alife.Client.Android;

// Build with -p:AlifeWindowProbe=true, start with adb shell am start -n
// com.alife.client/com.alife.client.WindowProbeActivity. No SDK/plugin setup is needed
// to exercise the actual app's overlay implementation. The entry point is absent in normal builds.
[Activity(Name = "com.alife.client.WindowProbeActivity", Exported = true)]
public sealed class WindowProbeActivity : Activity
{
    readonly List<IAlifeWindow> windows = [];
    string root = "";
    readonly Dictionary<string, Rectangle> actualBounds = [];
    readonly Dictionary<string, Rectangle> gestureBounds = [];

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(new TextView(this) { Text = "Alife window integration probe" });
        root = Path.Combine(FilesDir!.AbsolutePath, "window-probe");
        Directory.CreateDirectory(root);
        try {
            var manager = new AndroidWindowManager();
            foreach (var (name, bounds) in new[] {
                ("one", new Rectangle(20, 80, 260, 360)),
                ("two", new Rectangle(100, 460, 220, 220)),
            }) {
                var window = manager.Create(new AlifeWindowOptions {
                    Title = "Probe " + name, Url = new Uri(Path.Combine(root, "index.html")).AbsoluteUri + "#" + name,
                    Bounds = bounds, TopMost = true, Transparent = true,
                });
                windows.Add(window);
                actualBounds[name] = bounds;
                window.MessageReceived += json => HandleMessage(window, name, json);
                window.BoundsChanged += actual => { actualBounds[name] = actual; File.WriteAllText(Path.Combine(root, name + "-bounds.json"), JsonSerializer.Serialize(actual)); };
                await window.Launch();
            }
            StartForegroundService(new Intent(this, typeof(KeepAliveService)));
        } catch (Exception e) { File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); }
    }

    async void HandleMessage(IAlifeWindow window, string name, string json)
    {
        try {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement;
            if (!data.TryGetProperty("probe", out var action) && !data.TryGetProperty("type", out action)) return;
            switch (action.GetString()) {
                case "drag_start": case "resize_start": gestureBounds[name] = actualBounds[name]; break;
                case "drag_move":
                    var moved = gestureBounds[name];
                    moved.Offset(data.GetProperty("dx").GetInt32(), data.GetProperty("dy").GetInt32());
                    await window.SetBoundsAsync(moved); break;
                case "resize_delta":
                    var resized = gestureBounds[name];
                    resized.Width = Math.Max(150, resized.Width + data.GetProperty("dx").GetInt32());
                    resized.Height = Math.Max(150, resized.Height + data.GetProperty("dy").GetInt32());
                    await window.SetBoundsAsync(resized); break;
                case "bounds":
                    await window.SetBoundsAsync(new Rectangle(data.GetProperty("x").GetInt32(), data.GetProperty("y").GetInt32(),
                        data.GetProperty("width").GetInt32(), data.GetProperty("height").GetInt32()));
                    break;
                case "ignore": await window.SetIgnoreMouseEventsAsync(data.GetProperty("value").GetBoolean()); break;
                case "hide": await window.HideAsync(); break;
                case "show": await window.ShowAsync(); break;
                case "raise": await window.SetTopMostAsync(true); break;
                case "dispose": await window.DisposeAsync(); break;
                case "cursor":
                    AndroidPointerMonitor.EnsureStarted();
                    await window.PostMessageAsync(JsonSerializer.Serialize(new { type = "cursor",
                        x = AndroidAlifeClient.LastCursorPosition.X, y = AndroidAlifeClient.LastCursorPosition.Y }));
                    break;
                case "permission":
                    await ShizukuBridge.WaitForBinderAsync(TimeSpan.FromSeconds(5));
                    bool granted = await ShizukuBridge.RequestPermissionAsync();
                    await window.PostMessageAsync(JsonSerializer.Serialize(new { type = "permission", granted }));
                    AndroidPointerMonitor.EnsureStarted();
                    break;
                case "ready": File.WriteAllText(Path.Combine(root, name + "-ready.json"), json); break;
            }
        } catch (Exception e) { File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); }
    }
}

