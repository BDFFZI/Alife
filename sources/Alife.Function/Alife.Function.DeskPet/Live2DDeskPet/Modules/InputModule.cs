using System;
using System.Text.Json;

namespace Alife.Function.DeskPet;

public class InputModule : IPetModule, IDisposable
{
    public string CssCode => @"
#input-container {
    position:fixed; top:50%; left:50%;
    transform:translate(-50%, -50%) scale(var(--ui-scale));
    transform-origin:center center;
    width:220px; background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:20px;
    padding:4px 12px; display:flex; align-items:center;
    border:1px solid rgba(255,255,255,0.15);
    z-index:2000;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    opacity:0; transition:opacity 0.3s;
}
body:hover #input-container,
#input-container:focus-within { opacity:1; }
#chat-input {
    flex:1; background:transparent; border:none; outline:none;
    color:white; font-size:13px; padding:6px;
}
#chat-input::placeholder { color:rgba(255,255,255,0.5); }
#send-btn {
    background:#ffb7c5; border:none; border-radius:50%;
    width:26px; height:26px; cursor:pointer; margin-left:5px;
    display:flex; align-items:center; justify-content:center;
    color:white;
}
";
    public string HtmlCode => @"
<div id='input-container'>
    <input type='text' id='chat-input' placeholder='来聊聊吧...' autocomplete='off'>
    <button id='send-btn'>
        <svg viewBox='0 0 24 24' width='14' height='14' fill='currentColor'>
            <path d='M2.01 21L23 12 2.01 3 2 10l15 2-15 2z'/>
        </svg>
    </button>
</div>
";
    public string JsCode => @"
(function() {
    var input = document.getElementById('chat-input');
    var btn = document.getElementById('send-btn');
    var onSend = function() {
        var text = input.value.trim();
        if (text) {
            postMessage({type:'input', text:text});
            input.value = '';
        }
    };
    btn.onclick = onSend;
    input.onkeydown = function(e) { if (e.key === 'Enter') onSend(); };
})();
";

    readonly PetBridge bridge;
    readonly InputEventCallback onInput;

    public InputModule(PetBridge bridge, InputEventCallback onInput)
    {
        this.bridge = bridge;
        this.onInput = onInput;
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        if (type == "input")
        {
            string text = data.GetProperty("text").GetString() ?? "";
            onInput(text);
        }
    }
}