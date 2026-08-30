namespace Alife.Function.DeskPet;

public class UsingModule(PetBridge bridge) : IPetModule
{
    public string CssCode => @"
#thinking-indicator {
    position:fixed; top:50px; right:30px;
    display:flex; gap:4px;
    padding:8px 12px;
    background:rgba(255,255,255,0.8);
    backdrop-filter:blur(4px); border-radius:15px;
    box-shadow:0 2px 8px rgba(0,0,0,0.1);
    opacity:0;
    transform:scale(calc(0.8 * var(--ui-scale)));
    transform-origin:top right;
    transition:all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
    z-index:1500;
}
#thinking-indicator.show { opacity:1; transform:scale(var(--ui-scale)); }
.dot {
    width:6px; height:6px;
    background-color:#1890ff; border-radius:50%;
    animation:bounce 1.4s infinite ease-in-out both;
}
.dot:nth-child(1) { animation-delay:-0.32s; }
.dot:nth-child(2) { animation-delay:-0.16s; }
@keyframes bounce {
    0%, 80%, 100% { transform:scale(0); }
    40% { transform:scale(1); }
}
";
    public string HtmlCode => "<div id='thinking-indicator'><div class='dot'></div><div class='dot'></div><div class='dot'></div></div>";
    public string JsCode => @"
messageBus.on('status', (msg) => {
    if (msg.working) document.getElementById('thinking-indicator').classList.add('show');
    else document.getElementById('thinking-indicator').classList.remove('show');
});
";

    public void Send(bool working) => bridge.SendMessage("status", new { working });
}