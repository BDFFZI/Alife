using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

/// <summary>
/// 桌宠 Electron 浏览器窗口封装：负责创建透明置顶窗口、定位/移动/缩放、DPI 与脚本执行，
/// 并通过 StorageSystem 持久化/恢复窗口位置大小。
/// 网页资源位于插件 Resources/Live2D/（随插件分发或内嵌到客户端输出目录）。
/// </summary>
public sealed class PetWindow(StorageSystem storage) : IDisposable
{
    public event Action? MouseMoved;

    public BrowserWindow Window => window;
    public double Dpi => dpi;
    public Rectangle Bounds => bounds;
    public Point Position => position;
    public Point CursorScreenPoint => cursorScreenPoint;
    
    public void ResetBounds()
    {
        window.SetBounds(defaultBounds);
    }

    BrowserWindow window = null!;
    double dpi;
    Rectangle bounds = null!;
    Point position = null!;
    Point cursorScreenPoint = null!;
    CancellationTokenSource cancellationTokenSource = null!;

    const string WindowStateKey = "Live2DDeskPet/WindowState";
    Rectangle defaultBounds = null!;

    public async Task CreateAsync(string wwwRoot)
    {
        //获取基础属性
        Display primary = await Electron.Screen.GetPrimaryDisplayAsync();
        defaultBounds = new Rectangle {
            X = primary.WorkArea.X + primary.WorkArea.Width - 710,
            Y = primary.WorkArea.Y + primary.WorkArea.Height - 220,
            Width = 320,
            Height = 480,
        };

        dpi = primary.ScaleFactor;
        bounds = storage.GetObject<Rectangle>(WindowStateKey) ?? new Rectangle {
            X = defaultBounds.X,
            Y = defaultBounds.Y,
            Width = defaultBounds.Width,
            Height = defaultBounds.Height,
        };
        position = new Point {
            X = bounds.X + bounds.Width / 2,
            Y = bounds.Y + bounds.Height / 2,
        };
        cursorScreenPoint = await Electron.Screen.GetCursorScreenPointAsync();

        //加载窗口
        string url = new Uri(Path.Combine(wwwRoot, "index.html")).AbsoluteUri;
        if (OperatingSystem.IsWindows())
        {
            window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions {
                Title = "Live2D 桌宠",
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                AlwaysOnTop = true,
                SkipTaskbar = true,
                Frame = false,
                Transparent = true,
                HasShadow = false,
                Show = false,
                Movable = true,
                Resizable = false,
                Fullscreenable = false,
                BackgroundColor = "#00000000",
                WebPreferences = new WebPreferences {
                    NodeIntegration = true,
                    ContextIsolation = false,
                    Sandbox = false,
                    DevTools = true
                }
            }, url);
        }
        else
        {
            throw new NotSupportedException("不支持的平台。");
        }

        TaskCompletionSource tcs = new TaskCompletionSource();
        window.OnReadyToShow += () => {
            //提升窗口置顶层级到最高（screen-saver），确保盖过全屏/无边框窗口。
            window.SetAlwaysOnTop(true, (OnTopLevel)7, 1);
            window.Show();
            tcs.SetResult();
        };
        await tcs.Task;

        cancellationTokenSource = new CancellationTokenSource();
        Loop(cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
        storage.SetObject(WindowStateKey, bounds);
        window.Destroy();
    }

    async void Loop(CancellationToken cancellationToken = default)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(30, cancellationToken);

                bounds = await window.GetBoundsAsync();
                position.X = bounds.X + bounds.Width / 2;
                position.Y = bounds.Y + bounds.Height / 2;
                Point newCursorPoint = await Electron.Screen.GetCursorScreenPointAsync();
                if (cursorScreenPoint.X != newCursorPoint.X || cursorScreenPoint.Y != newCursorPoint.Y)
                {
                    cursorScreenPoint = newCursorPoint;
                    MouseMoved?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            AlifeLog.LogError(e);
        }
    }
}