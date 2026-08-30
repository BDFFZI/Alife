using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

public class GazeModule : IPetModule, IDisposable
{
    public string JsCode => "messageBus.on('look', (msg) => model.focus(msg.x, msg.y, msg.instant));";

    readonly PetBridge bridge;
    readonly PetWindow window;
    readonly CancellationTokenSource? cancellationTokenSource;
    long lastMouseMoveTime;

    public GazeModule(PetBridge bridge, MouseTracker tracker, PetWindow window)
    {
        this.bridge = bridge;
        this.window = window;
        tracker.MouseMoved += (x, y) => {
            lastMouseMoveTime = Now();
            (double scaleX, double scaleY) dpi = window.GetDpi();
            (double left, double top, double width, double height) layout = window.GetLayout();
            bridge.SendMessage("look", new { x = x / dpi.scaleX - layout.left, y = y / dpi.scaleY - layout.top, instant = false });
        };
        cancellationTokenSource = new CancellationTokenSource();
        _ = FocusResetLoop(cancellationTokenSource.Token);
    }
    public void Dispose()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    async Task FocusResetLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, cancellationToken);
                if (Now() - lastMouseMoveTime > 3000)
                {
                    (double left, double top, double width, double height) layout = window.GetLayout();
                    bridge.SendMessage("look", new { x = layout.width / 2, y = layout.height / 2, instant = false });
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    static long Now() => DateTimeOffset.Now.ToUnixTimeMilliseconds();
}