using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.DeskPet;

public class SubtitleModule : IPetModule, IDisposable
{
    public string CssCode => @"
#bubble-container {
    position:fixed; top:2%; left:50%;
    transform:translateX(-50%) scale(var(--ui-scale));
    transform-origin:top center; width:250px;
    pointer-events:none; z-index:1000;
    opacity:0; transition:opacity 0.3s ease;
}
#bubble-container.show { opacity:1; }
#bubble {
    background:rgba(255,255,255,0.95);
    backdrop-filter:blur(8px); border-radius:18px;
    padding:12px 16px; font-family:'Microsoft YaHei',sans-serif;
    font-size:14px; color:#444;
    box-shadow:0 4px 15px rgba(0,0,0,0.1);
    border:1px solid rgba(255,255,255,0.5);
    position:relative; line-height:1.5;
}
#bubble::after {
    content:''; position:absolute; bottom:-8px; left:50%;
    transform:translateX(-50%);
    border-left:8px solid transparent;
    border-right:8px solid transparent;
    border-top:8px solid rgba(255,255,255,0.95);
}
";

    public string HtmlCode => "<div id='bubble-container'><div id='bubble'></div></div>";

    public string JsCode => @"
messageBus.on('bubble', (msg) => {
    document.getElementById('bubble').innerText = msg.text;
    document.getElementById('bubble-container').classList.add('show');
});
messageBus.on('hide-bubble', () => {
    document.getElementById('bubble-container').classList.remove('show');
});
";

    public void Show(string text)
    {
        lastTime = Now();
        bridge.SendMessage("bubble", new { text });
    }

    public void Hide() => bridge.SendMessage("hide-bubble");

    readonly PetBridge bridge;
    readonly CancellationTokenSource? cancellationTokenSource;
    long? lastTime;

    public SubtitleModule(PetBridge bridge)
    {
        this.bridge = bridge;
        cancellationTokenSource = new CancellationTokenSource();
        _ = AutoHideLoop(cancellationTokenSource.Token);
    }
    public void Dispose()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }


    async Task AutoHideLoop(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(1000, ct);
                if (lastTime != null && Now() - lastTime > 6000)
                {
                    Hide();
                    lastTime = null;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    static long Now() => DateTimeOffset.Now.ToUnixTimeMilliseconds();
}