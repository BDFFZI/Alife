using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Client;
using Alife.Framework;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Auditory;

public sealed class AuditoryOverlayWindow(
    AuditoryService auditoryService,
    AudioRecognitionService? audioRecognitionService,
    IAlifeApplication alifeApplication,
    PluginSystem pluginSystem,
    StorageSystem storageSystem,
    ILogger logger) : IAsyncDisposable
{
    const string PluginId = "Alife.Function.Auditory";
    const int OverlayWidth = 132;
    const int OverlayHeight = 64;

    public async Task ResetWindowAsync()
    {
        if (window == null)
            return;

        bounds = defaultBounds;
        await SetBoundsSafeAsync(bounds);
        SaveBounds();
    }

    public async Task CreateAsync(Character character, CancellationToken cancellationToken)
    {
        boundsKey = $"{character.StorageKey}/AuditoryOverlay/WindowBounds";
        try
        {
            float dpi = Math.Max(0.1f, alifeApplication.GetDpiScale());
            Size screen = alifeApplication.GetScreenSize();
            int screenWidth = Math.Max(1, (int)(screen.Width / dpi));
            int screenHeight = Math.Max(1, (int)(screen.Height / dpi));
            defaultBounds = new Rectangle(
                Math.Max(0, screenWidth - OverlayWidth - 24),
                Math.Max(0, screenHeight - OverlayHeight - 72),
                OverlayWidth,
                OverlayHeight);
            bounds = storageSystem.GetObject(boundsKey, defaultBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                bounds = defaultBounds;
            bounds.Width = OverlayWidth;
            bounds.Height = OverlayHeight;
            bounds.X = Math.Clamp(bounds.X, 0, Math.Max(0, screenWidth - bounds.Width));
            bounds.Y = Math.Clamp(bounds.Y, 0, Math.Max(0, screenHeight - bounds.Height));

            string wwwRoot = Path.Combine(
                pluginSystem.PluginContext.GetPluginDirectoryPath(PluginId),
                "Resources",
                "AuditoryOverlay");
            string indexPath = Path.Combine(wwwRoot, "index.html");
            if (File.Exists(indexPath) == false)
                throw new FileNotFoundException("听觉控制悬浮窗页面不存在。", indexPath);

            window = await alifeApplication.CreateWindowAsync(new AlifeWindowOptions {
                Title = "听觉控制",
                Url = new Uri(indexPath).AbsoluteUri,
                Bounds = bounds,
                TopMost = true,
                Resizable = false,
                Transparent = true,
            });
            window.MessageReceived += OnMessageReceived;
            window.BoundsChanged += OnBoundsChanged;
            await window.Launch();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await readyTcs.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
            {
                logger.LogWarning("听觉控制悬浮窗未在超时前完成初始化，将继续显示。");
            }

            await SendStateAsync(force: true);
            loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = LoopAsync(loopCancellation.Token);
        }
        catch
        {
            await DisposeWindowAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;

        if (loopCancellation != null)
        {
            await loopCancellation.CancelAsync();
            loopCancellation.Dispose();
            loopCancellation = null;
        }

        await StopTemporaryAsync();
        SaveBounds();
        await DisposeWindowAsync();
    }

    void OnMessageReceived(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProperty) == false)
                return;

            string? type = typeProperty.GetString();
            switch (type)
            {
                case "ready":
                    lastStateJson = "";
                    readyTcs.TrySetResult(true);
                    break;
                case "toggle" when TryGetTarget(root, out string? target):
                    _ = RunGuardedAsync(() => ToggleAsync(target));
                    break;
                case "hold_start" when TryGetTarget(root, out string? target):
                    _ = RunGuardedAsync(() => HoldStartAsync(target));
                    break;
                case "hold_end" when TryGetTarget(root, out string? target):
                    _ = RunGuardedAsync(() => HoldEndAsync(target));
                    break;
                case "drag_start":
                    dragStartBounds = bounds;
                    dragging = true;
                    break;
                case "drag_move":
                    if (dragging == false)
                        break;
                    int dx = root.TryGetProperty("dx", out JsonElement dxProperty) ? dxProperty.GetInt32() : 0;
                    int dy = root.TryGetProperty("dy", out JsonElement dyProperty) ? dyProperty.GetInt32() : 0;
                    Rectangle moved = dragStartBounds;
                    moved.X += dx;
                    moved.Y += dy;
                    bounds = moved;
                    _ = SetBoundsSafeAsync(moved);
                    break;
                case "drag_end":
                    if (dragging)
                    {
                        dragging = false;
                        SaveBounds();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理听觉控制悬浮窗消息失败。");
        }
    }

    async Task ToggleAsync(string target)
    {
        if (target == "system")
        {
            if (audioRecognitionService == null)
                return;

            await systemGate.WaitAsync();
            try
            {
                await audioRecognitionService.SetAudioListening(audioRecognitionService.IsListeningActive == false);
            }
            finally
            {
                systemGate.Release();
            }
        }
        else if (target == "mic")
        {
            if (auditoryService == null)
                return;

            await micGate.WaitAsync();
            try
            {
                if (auditoryService.IsRunning)
                    auditoryService.StopRecording();
                else
                    await auditoryService.StartRecordingAsync();
            }
            finally
            {
                micGate.Release();
            }
        }

        await SendStateAsync(force: true);
    }

    async Task HoldStartAsync(string target)
    {
        if (target == "system")
        {
            if (audioRecognitionService == null)
                return;

            await systemGate.WaitAsync();
            try
            {
                if (systemHoldActive)
                    return;

                systemHoldActive = true;
                systemHoldWasActive = audioRecognitionService.IsListeningActive;
                if (systemHoldWasActive == false)
                {
                    systemTemporary = true;
                    await audioRecognitionService.SetAudioListening(true);
                }
            }
            catch
            {
                systemHoldActive = false;
                systemTemporary = false;
                throw;
            }
            finally
            {
                systemGate.Release();
            }
        }
        else if (target == "mic")
        {
            if (auditoryService == null)
                return;

            await micGate.WaitAsync();
            try
            {
                if (micHoldActive)
                    return;

                micHoldActive = true;
                await auditoryService.BeginTemporaryMicAsync();
            }
            catch
            {
                micHoldActive = false;
                throw;
            }
            finally
            {
                micGate.Release();
            }
        }

        await SendStateAsync(force: true);
    }

    async Task HoldEndAsync(string target)
    {
        if (target == "system")
        {
            if (audioRecognitionService == null)
                return;

            await systemGate.WaitAsync();
            try
            {
                if (systemHoldActive == false)
                    return;

                systemHoldActive = false;
                if (systemTemporary)
                {
                    systemTemporary = false;
                    if (audioRecognitionService.IsListeningActive)
                        await audioRecognitionService.SetAudioListening(false);
                }
            }
            finally
            {
                systemGate.Release();
            }
        }
        else if (target == "mic")
        {
            if (auditoryService == null)
                return;

            await micGate.WaitAsync();
            try
            {
                if (micHoldActive == false)
                    return;

                micHoldActive = false;
                await auditoryService.EndTemporaryMicAsync();
            }
            finally
            {
                micGate.Release();
            }
        }

        await SendStateAsync(force: true);
    }

    async Task StopTemporaryAsync()
    {
        bool stopSystem = systemTemporary;
        systemTemporary = false;
        systemHoldActive = false;
        if (stopSystem && audioRecognitionService?.IsListeningActive == true)
        {
            try
            {
                await audioRecognitionService.SetAudioListening(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "关闭系统声音接收失败。");
            }
        }

        if (micHoldActive)
        {
            micHoldActive = false;
            try
            {
                await auditoryService.EndTemporaryMicAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "关闭麦克风接收失败。");
            }
        }
    }

    async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await SendStateAsync();
                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "听觉控制悬浮窗状态同步失败。");
        }
    }

    async Task SendStateAsync(bool force = false)
    {
        if (window == null)
            return;

        bool systemAvailable = audioRecognitionService != null;
        bool micAvailable = auditoryService != null;
        string json = JsonSerializer.Serialize(new {
            type = "state",
            system = new {
                available = systemAvailable,
                active = systemAvailable && audioRecognitionService!.IsListeningActive,
                temporary = systemTemporary,
            },
            mic = new {
                available = micAvailable,
                active = micAvailable && auditoryService!.IsRunning,
                listening = micAvailable && auditoryService!.IsListening,
                temporary = micAvailable && auditoryService!.IsTemporaryMicActive,
            },
        });

        if (force == false && json == lastStateJson)
            return;

        try
        {
            await window.PostMessageAsync(json);
            lastStateJson = json;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送听觉控制悬浮窗状态失败。");
        }
    }

    async Task RunGuardedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行听觉控制操作失败。");
        }
    }

    async Task SetBoundsSafeAsync(Rectangle value)
    {
        try
        {
            if (window != null)
                await window.SetBoundsAsync(value);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "调整听觉控制悬浮窗位置失败。");
        }
    }

    void OnBoundsChanged(Rectangle actualBounds)
    {
        bounds = actualBounds;
    }

    void SaveBounds()
    {
        if (string.IsNullOrEmpty(boundsKey))
            return;

        try
        {
            storageSystem.SetObject(boundsKey, bounds);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "保存听觉控制悬浮窗位置失败。");
        }
    }

    async Task DisposeWindowAsync()
    {
        if (window == null)
            return;

        window.MessageReceived -= OnMessageReceived;
        window.BoundsChanged -= OnBoundsChanged;
        try
        {
            await window.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "关闭听觉控制悬浮窗失败。");
        }
        window = null;
    }

    static bool TryGetTarget(JsonElement root, [NotNullWhen(true)] out string? target)
    {
        target = root.TryGetProperty("target", out JsonElement targetProperty) ? targetProperty.GetString() : null;
        return target is "system" or "mic";
    }

    string boundsKey = "";
    bool disposed;

    readonly SemaphoreSlim systemGate = new(1, 1);
    readonly SemaphoreSlim micGate = new(1, 1);
    readonly TaskCompletionSource<bool> readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    IAlifeWindow? window;
    CancellationTokenSource? loopCancellation;
    Rectangle bounds;
    Rectangle defaultBounds;
    Rectangle dragStartBounds;
    bool dragging;
    bool systemTemporary;
    bool systemHoldActive;
    bool systemHoldWasActive;
    bool micHoldActive;
    string lastStateJson = "";
}
