using System;
using System.Text.Json;
using Alife.Framework;

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
#input-container.off { display:none; }
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
#input-toggle-btn {
    position:fixed; right:15px; bottom:88px;
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:pointer; z-index:2000;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.15);
    opacity:0; transition:opacity 0.3s, background 0.3s, opacity 0.3s;
}
body:hover #input-toggle-btn { opacity:1; }
#input-toggle-btn:hover { background:rgba(0,0,0,0.6); }
#input-toggle-btn.faded {
    filter:grayscale(1);
    background:rgba(0,0,0,0.22);
    border-color:rgba(255,255,255,0.1);
}
#input-toggle-btn.faded:hover { background:rgba(0,0,0,0.35); }
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
<div id='input-toggle-btn' title='输入框开关'>
    <svg viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>
        <path d='M20 2H4c-1.1 0-1.99.9-1.99 2L2 22l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-2 12H6v-2h12v2zm0-3H6V9h12v2zm0-3H6V6h12v2z'/>
    </svg>
</div>
";
    public string JsCode => @"
(function() {
    var input = document.getElementById('chat-input');
    var btn = document.getElementById('send-btn');
    var container = document.getElementById('input-container');
    var toggleBtn = document.getElementById('input-toggle-btn');
    var onSend = function() {
        var text = input.value.trim();
        if (text) {
            postMessage({type:'input', text:text});
            input.value = '';
        }
    };
    btn.onclick = onSend;
    input.onkeydown = function(e) { if (e.key === 'Enter') onSend(); };

    messageBus.on('input_state', function(msg) {
        container.classList.toggle('off', !msg.on);
        toggleBtn.classList.toggle('faded', !msg.on);
    });
    toggleBtn.addEventListener('click', function() { postMessage({type:'input_toggle'}); });

    postMessage({type:'input_toggle_ready'});
})();
";

    readonly PetBridge bridge;
    readonly InputEventCallback onInput;
    readonly StorageSystem storage;
    readonly string inputEnabledKey;
    bool inputEnabled = true;

    public InputModule(PetBridge bridge, InputEventCallback onInput, StorageSystem storage, PetStorageKey storageKey)
    {
        this.bridge = bridge;
        this.onInput = onInput;
        this.storage = storage;
        inputEnabledKey = $"{storageKey.Value}/Live2DDeskPet/InputEnabled";
        inputEnabled = storage.GetObject(inputEnabledKey, true);
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        switch (type)
        {
            case "input":
                if (inputEnabled)
                {
                    string text = data.GetProperty("text").GetString() ?? "";
                    onInput(text);
                }
                break;
            case "input_toggle_ready":
                bridge.SendMessage("input_state", new { on = inputEnabled });
                break;
            case "input_toggle":
                inputEnabled = !inputEnabled;
                storage.SetObject(inputEnabledKey, inputEnabled);
                bridge.SendMessage("input_state", new { on = inputEnabled });
                break;
        }
    }
}