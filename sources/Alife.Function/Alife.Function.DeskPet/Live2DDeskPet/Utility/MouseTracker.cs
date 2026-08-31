using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

/// <summary>
/// 全局鼠标追踪器：监听系统级鼠标移动事件。
/// </summary>
public class MouseTracker : IDisposable
{
    public event Action<int, int>? MouseMoved;

    readonly CancellationTokenSource cancellationTokenSource;
    readonly int[] lastPoint = new int[2];
    readonly double dpi;

    public MouseTracker()
    {
        dpi = Electron.Screen.GetPrimaryDisplayAsync().Result.ScaleFactor;
        cancellationTokenSource = new CancellationTokenSource();
        Loop();
    }
    public void Dispose()
    {
        cancellationTokenSource.Cancel();
    }

    async void Loop(CancellationToken cancellationToken = default)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(30, cancellationToken);
                Point point = await Electron.Screen.GetCursorScreenPointAsync();
                int newX = (int)(point.X * dpi);
                int newY = (int)(point.Y * dpi);

                if (lastPoint[0] != newX || lastPoint[1] != newY)
                {
                    lastPoint[0] = newX;
                    lastPoint[1] = newY;
                    MouseMoved?.Invoke(newX, newY);
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