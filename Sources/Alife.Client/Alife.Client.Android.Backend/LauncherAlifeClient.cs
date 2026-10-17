using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;

namespace Alife.Client.Android.Backend;

public sealed class LauncherAlifeClient(NativeHostBridge bridge) : IAlifeClient
{
    readonly ConcurrentDictionary<string, Type> components = new();
    internal Type GetWindowComponent(string id) => components.TryGetValue(id, out var component)
        ? component : throw new InvalidOperationException("The window has been disposed.");
    public Task SetupEnvironment() => Task.CompletedTask;
    public Task StopAsync()
    {
        Exit();
        return Task.CompletedTask;
    }
    public IReadOnlyList<EnvironmentItem> GetEnvironmentItems()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        bool ready = !string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, "sdk"))
            && Directory.EnumerateDirectories(Path.Combine(root, "sdk"), "10.*").Any();
        return [new("dotnet", ".NET SDK 10", ready, true)];
    }
    public Task InstallEnvironmentAsync(string id, IProgress<string>? progress = null)
    {
        if (id != "dotnet") throw new ArgumentException("Unknown environment item.", nameof(id));
        if (!GetEnvironmentItems()[0].IsReady)
            throw new InvalidOperationException("请重启客户端，由启动器安装 .NET SDK。");
        progress?.Report(".NET SDK 已由启动器安装。");
        return Task.CompletedTask;
    }
    public void Quit() => _ = bridge.SendAsync("hide-main");
    public void Exit() => _ = bridge.SendAsync("exit");
    public Task<string?> SelectFolderAsync() => throw new NotSupportedException();
    public bool GetAutoStart() => false;
    public void SetAutoStart(bool value) => throw new NotSupportedException();
    public Task UpgradeClient(string version, Action<int>? onProgress = null) => throw new NotSupportedException();
    public Task RevealFolderAsync(string path) => throw new NotSupportedException();

    public async Task<IAlifeWindow> CreateWindowAsync(AlifeWindowOptions options)
    {
        string id = Guid.NewGuid().ToString("N");
        string? url = null;
        HtmlWebWindowOptions hostOptions;
        if (options is RazorWebWindowOptions razorOptions)
        {
            Type component = razorOptions.RootComponent;
            if (!typeof(IComponent).IsAssignableFrom(component)) throw new ArgumentException("RootComponent must be a Razor component.", nameof(options));
            components[id] = component;
            url = $"/host/windows/{id}";
            hostOptions = new HtmlWebWindowOptions {
                Url = url,
                Title = options.Title, Width = options.Width, Height = options.Height,
                TopMost = options.TopMost, Resizable = options.Resizable,
                Transparent = options.Transparent, ShowInTaskbar = options.ShowInTaskbar,
            };
        }
        else if (options is HtmlWebWindowOptions htmlOptions) hostOptions = htmlOptions;
        else throw new NotSupportedException($"Unsupported window options: {options.GetType().Name}");
        try { await bridge.SendAsync("create", id, hostOptions, url); }
        catch { components.TryRemove(id, out _); throw; }
        return new LauncherWindow(this, bridge, id);
    }

    sealed class LauncherWindow(LauncherAlifeClient owner, NativeHostBridge bridge, string id) : IAlifeWindow
    {
        public Task ShowAsync() => bridge.SendAsync("show", id);
        public Task HideAsync() => bridge.SendAsync("hide", id);
        public Task NavigateAsync(string url) => bridge.SendAsync("navigate", id, url: url);
        public async ValueTask DisposeAsync()
        {
            await bridge.SendAsync("dispose", id);
            owner.components.TryRemove(id, out _);
        }
    }
}
