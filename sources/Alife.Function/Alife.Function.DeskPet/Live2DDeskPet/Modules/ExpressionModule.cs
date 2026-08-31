using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Function.DeskPet;

public class ExpressionModule : IPetModule, IDisposable
{
    public string JsCode => @"
messageBus.on('expression', (msg) => model.expression(msg.id));
messageBus.on('motion', (msg) => model.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE));
";

    public void PlayExpression(string? id)
    {
        bridge.SendMessage("expression", new { id });
        lastPlayExpressionTime = DateTime.Now;
    }

    public void PlayMotion(string group, int index)
    {
        bridge.SendMessage("motion", new { group, index });
    }

    readonly PetBridge bridge;
    readonly PetModelMetadata metadata;
    readonly CancellationTokenSource? cancellationTokenSource;
    DateTime? lastPlayExpressionTime;

    public ExpressionModule(PetBridge bridge, PetModelMetadata metadata)
    {
        this.bridge = bridge;
        this.metadata = metadata;
        cancellationTokenSource = new CancellationTokenSource();

        if (metadata.Expressions.Count != 0)
            AutoRevertLoop(cancellationTokenSource.Token);
    }
    public void Dispose()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    async void AutoRevertLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                await Task.Delay(500, cancellationToken);
                if (lastPlayExpressionTime == null)
                    continue;

                if (DateTime.Now - lastPlayExpressionTime.Value > TimeSpan.FromSeconds(3))
                {
                    PlayExpression(metadata.Expressions.First());
                    lastPlayExpressionTime = null;
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