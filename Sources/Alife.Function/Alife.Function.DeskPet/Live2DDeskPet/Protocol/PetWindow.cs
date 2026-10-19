using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Client;
using Alife.Foundation;
using Alife.Framework;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Alife.Function.DeskPet;

/// <summary>按角色隔离的存储键前缀，值为角色的 StorageKey（如 "Character\Mao"）。</summary>
public record PetStorageKey(string Value);

/// <summary>
/// 桌宠 Electron 浏览器窗口封装：负责创建透明置顶窗口、定位/移动/缩放、DPI 与脚本执行，
/// 并通过 StorageSystem 持久化/恢复窗口位置大小（按角色隔离）。
/// 网页资源位于插件 Resources/Live2D/（随插件分发或内嵌到客户端输出目录）。
/// </summary>
public sealed class PetWindow(IAlifeApplication alifeApplication, StorageSystem storage, PetStorageKey storageKey, ILogger<Live2DDeskPet> logger)
    : IAsyncDisposable
{
    public event Action<string, JsonElement>? OnMessage;
    public event Action? MouseMoved;

    public IAlifeWindow Window => window ?? throw new InvalidOperationException();
    public Rectangle Bounds => bounds;
    public Point Position => position;
    public float Dpi => dpi;
    public Point CursorScreenPoint => cursorScreenPoint;

    public async Task ResetBoundsAsync()
    {
        await SetBoundsAsync(defaultBounds);
    }
    public async Task SetBoundsAsync(Rectangle bounds)
    {
        this.bounds = bounds;
        position = new Point {
            X = bounds.X + bounds.Width / 2,
            Y = bounds.Y + bounds.Height / 2,
        };
        if (window != null)
            await window.SetBoundsAsync(bounds);
    }

    public void SendMessage(string type, object? payload = null)
    {
        try
        {
            JObject envelope = new() { ["type"] = type };
            if (payload != null)
            {
                JObject payloadObj = JObject.FromObject(payload);
                foreach (KeyValuePair<string, JToken?> kvp in payloadObj)
                    envelope[kvp.Key] = kvp.Value;
            }
            window?.PostMessageAsync(envelope.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送桌宠消息失败");
        }
    }
    public async Task<string?> ExecuteJavaScriptAsync(string script)
    {
        try
        {
            if (window != null)
                return await window.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行桌宠脚本失败");
        }

        return null;
    }

    IAlifeWindow? window;
    CancellationTokenSource? cancellationTokenSource;
    Rectangle bounds;
    Point position;
    float dpi;
    Point cursorScreenPoint;

    readonly string windowBoundsKey = $"{storageKey.Value}/Live2DDeskPet/WindowBounds";
    Rectangle defaultBounds;

    public async Task CreateAsync(string wwwRoot)
    {
        // Initialize DPI before deriving screen dimensions or the first cursor sample.
        dpi = alifeApplication.GetDpiScale();
        Size screenSize = (alifeApplication.GetScreenSize() / dpi).ToSize();
        defaultBounds = new Rectangle {
            X = Math.Max(0, screenSize.Width - Math.Min(320, screenSize.Width) - 20),
            Y = Math.Max(0, screenSize.Height - Math.Min(480, screenSize.Height) - 20),
            Width = Math.Min(320, screenSize.Width),
            Height = Math.Min(480, screenSize.Height),
        };
        await SetBoundsAsync(storage.GetObject(windowBoundsKey, defaultBounds));
        cursorScreenPoint = GetCursorPosition();

        //创建窗口
        string url = $"{new Uri(Path.Combine(wwwRoot, "index.html")).AbsoluteUri}";
        window = await alifeApplication.CreateWindowAsync(new AlifeWindowOptions {
            Title = "Live2D 桌宠",
            Bounds = bounds,
            TopMost = true,
            Resizable = false,
            Transparent = true,
            Url = url
        });
        window.MessageReceived += OnIpcMessage;
        window.BoundsChanged += OnBoundsChanged;

        //注册 init 监听：必须在窗口创建（开始加载页面）之前完成，避免错过渲染进程在脚本末尾发送的 init 消息
        TaskCompletionSource initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnInit(string type, JsonElement _)
        {
            if (type == "init")
            {
                OnMessage -= OnInit;
                initTcs.TrySetResult();
            }
        }
        OnMessage += OnInit;

        //唤醒窗口
        await window.Launch();

        //等待渲染进程初始化完成，确保 JS 已加载并能通讯后再继续后续消息交互
        using CancellationTokenSource initTimeout = new(TimeSpan.FromSeconds(30));
        await initTcs.Task.WaitAsync(initTimeout.Token);

        //检测鼠标变动
        cancellationTokenSource = new CancellationTokenSource();
        Loop(cancellationTokenSource.Token);
    }
    void OnBoundsChanged(Rectangle actualBounds)
    {
        // Android clamps restored/off-screen windows and reflows them on rotation.
        bounds = actualBounds;
        position = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        dpi = alifeApplication.GetDpiScale();
    }

    public async ValueTask DisposeAsync()
    {
        storage.SetObject(windowBoundsKey, bounds);

        if (cancellationTokenSource != null)
            await cancellationTokenSource.CancelAsync();
        if (window != null)
        {
            window.BoundsChanged -= OnBoundsChanged;
            window.MessageReceived -= OnIpcMessage;
            await window.DisposeAsync();
        }
    }

    async void Loop(CancellationToken cancellationToken = default)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(30, cancellationToken);

                try
                {
                    Point newCursorPoint = GetCursorPosition();
                    if (cursorScreenPoint.X != newCursorPoint.X || cursorScreenPoint.Y != newCursorPoint.Y)
                    {
                        cursorScreenPoint = newCursorPoint;
                        MouseMoved?.Invoke();
                    }
                }
                catch (TimeoutException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
    void OnIpcMessage(object? payload)
    {
        // logger.LogInformation(payload?.ToString());

        if (payload is not string json)
            return;
        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement typeProp) == false)
                return;

            string? type = typeProp.GetString();
            if (type == null)
                return;

            try
            {
                OnMessage?.Invoke(type, root);
            }
            catch (Exception e)
            {
                logger.LogError(e, "执行桌宠前端消息回调失败");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解析桌宠前端消息失败");
        }
    }
    Point GetCursorPosition()
    {
        dpi = alifeApplication.GetDpiScale();
        Point newCursorPoint = alifeApplication.GetCursorPosition();
        newCursorPoint.X = (int)(newCursorPoint.X / dpi);
        newCursorPoint.Y = (int)(newCursorPoint.Y / dpi);
        return newCursorPoint;
    }
}