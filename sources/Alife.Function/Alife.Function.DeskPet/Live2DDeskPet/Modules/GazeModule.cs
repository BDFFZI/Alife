using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using ElectronNET.API.Entities;

namespace Alife.Function.DeskPet;

public class GazeModule : IPetModule, IDisposable
{
    public string JsCode => "messageBus.on('look', (msg) => model.focus(msg.x, msg.y, msg.instant));";

    readonly PetBridge bridge;
    readonly PetWindow window;
    readonly CancellationTokenSource? cancellationTokenSource;
    DateTime? lastMouseMoveTime;

    public GazeModule(PetBridge bridge, PetWindow window)
    {
        this.bridge = bridge;
        this.window = window;

        window.MouseMoved += OnMouseMoved;
        cancellationTokenSource = new CancellationTokenSource();
        FocusResetLoop(cancellationTokenSource.Token);
    }

    public void Dispose()
    {
        window.MouseMoved -= OnMouseMoved;
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    void OnMouseMoved()
    {
        Point point = window.CursorScreenPoint;
        Rectangle rectangle = window.Bounds;
        bridge.SendMessage("look", new {
            x = (point.X - rectangle.X) * window.Dpi, y = (point.Y - rectangle.Y) * window.Dpi, instant = false
        });
        lastMouseMoveTime = DateTime.Now;
    }
    async void FocusResetLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(500, cancellationToken);
                if (lastMouseMoveTime == null)
                    continue;

                if (DateTime.Now - lastMouseMoveTime.Value > TimeSpan.FromSeconds(3))
                {
                    bridge.SendMessage("look", new {
                        x = window.Bounds.Width / 2,
                        y = window.Bounds.Height / 2,
                        instant = false
                    });
                    lastMouseMoveTime = null;
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