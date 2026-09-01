using System;
using System.Text.Json;
using Alife.Framework;

namespace Alife.Function.DeskPet;

/// <summary>
/// 移动/缩放模块：在右下角提供一个圆形控制按钮，悬停时展开二级菜单，
/// 菜单包含 移动 / 缩放 / 帮助 三个圆形按钮。
/// 移动按钮拖拽移动桌宠、缩放按钮拖拽缩放桌宠；双击对应按钮只重置位移或只重置缩放。
/// 帮助按钮悬停显示操作指南。用户设置的缩放/偏移通过 <see cref="StorageSystem"/> 持久化，
/// 桌宠就绪时恢复（参考鼠标穿透模块的存储方式）。
/// </summary>
/// <summary>用户设置的模型缩放与偏移（持久化）。</summary>
public record MoveScaleState
{
    public double Scale { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
}

public class MoveScaleModule : IPetModule, IDisposable
{
    public string CssCode => @"
#pet-controls {
    position:fixed; right:15px; bottom:52px;
    display:flex; flex-direction:row; align-items:center; gap:6px;
    z-index:2100;
}
#pet-btn {
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:pointer;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.15);
    opacity:0; transition:opacity 0.3s, background 0.2s, transform 0.1s;
}
body:hover #pet-btn { opacity:1; }
#pet-btn:hover { background:rgba(0,0,0,0.6); }
#pet-btn:active { transform:scale(0.9); }

#pet-menu {
    display:flex; flex-direction:row; align-items:center; gap:6px;
    opacity:0; pointer-events:none; transition:opacity 0.2s;
}
#pet-controls:hover #pet-menu, body.pet-dragging #pet-menu {
    opacity:1; pointer-events:auto;
}
.pet-item {
    width:28px; height:28px;
    background:rgba(0,0,0,0.4);
    backdrop-filter:blur(10px); border-radius:50%;
    display:flex; justify-content:center; align-items:center;
    color:white; cursor:pointer;
    box-shadow:0 4px 10px rgba(0,0,0,0.2);
    border:1px solid rgba(255,255,255,0.15);
    transition:background 0.2s, transform 0.1s;
}
.pet-item:hover { background:rgba(0,0,0,0.6); }
.pet-item:active { transform:scale(0.9); }
#pet-move-btn { cursor:move; }
#pet-scale-btn { cursor:nwse-resize; }

#pet-guide {
    position:fixed; right:88px; bottom:8px; width:190px;
    background:rgba(0,0,0,0.65);
    backdrop-filter:blur(8px); color:white;
    border-radius:8px; padding:10px 12px; font-size:12px; line-height:1.7;
    border:1px solid rgba(255,255,255,0.15);
    box-shadow:0 4px 12px rgba(0,0,0,0.3);
    opacity:0; pointer-events:none; transition:opacity 0.2s; z-index:2200;
}
#pet-guide.show { opacity:1; }
";
    public string HtmlCode => @"
<div id='pet-controls'>
    <div id='pet-menu'>
        <div id='pet-help-btn' class='pet-item' title='操作指南'>
            <svg viewBox='0 0 24 24' width='14' height='14' fill='currentColor'>
                <path d='M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z'/>
            </svg>
        </div>
        <div id='pet-scale-btn' class='pet-item' title='拖动缩放，双击重置缩放'>
            <svg viewBox='0 0 24 24' width='14' height='14' fill='currentColor'>
                <path d='M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z'/>
            </svg>
        </div>
        <div id='pet-move-btn' class='pet-item' title='拖动移动，双击重置位移'>
            <svg viewBox='0 0 24 24' width='14' height='14' fill='currentColor'>
                <path d='M13 6.83V2h-2v4.83L8.66 4.5 7.25 5.91 12 10.66l4.75-4.75-1.41-1.41L13 6.83zM2 11h4.83L4.5 8.66l1.41-1.41L10.66 12l-4.75 4.75-1.41-1.41L6.83 13H2v-2zm9 9.17V22h2v-4.83l2.34 2.34 1.41-1.41L12 13.34l-4.75 4.75 1.41 1.41L11 17.17zM14 11h4.83l-2.34-2.34 1.41-1.41L22 12l-4.1 4.1-1.41-1.41L18.83 13H14v-2z'/>
            </svg>
        </div>
    </div>
    <div id='pet-btn' title='移动 / 缩放'>
        <svg viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>
            <path d='M12 1l-3 3h2v4h2V4h2l-3-3zM3 12l3 3v-2h4v-2H6V9l-3 3zM12 23l3-3h-2v-4h-2v4H9l3 3zM21 12l-3-3v2h-4v2h4v2l3-3z'/>
        </svg>
    </div>
</div>
<div id='pet-guide'>
    <b>操作指南</b>
    <div>&#9995; 移动按钮：拖拽移动桌宠，双击重置位移</div>
    <div>&#8657; 缩放按钮：拖拽缩放桌宠，双击重置缩放</div>
    <div>&#10067; 帮助按钮：查看本指南</div>
</div>
";
    public string JsCode => @"
(function() {
    var guide = document.getElementById('pet-guide');
    var helpBtn = document.getElementById('pet-help-btn');
    var guideHover = false;

    function showGuide() { guide.classList.add('show'); }
    function hideGuide() { if (!guideHover) guide.classList.remove('show'); }

    if (helpBtn) {
        helpBtn.addEventListener('mouseenter', function() { guideHover = true; showGuide(); });
        helpBtn.addEventListener('mouseleave', function() { guideHover = false; hideGuide(); });
    }

    function sendEnd(type) {
        postMessage({type:type, scale:userScale, offsetX:userOffsetX, offsetY:userOffsetY});
    }

    var moveBtn = document.getElementById('pet-move-btn');
    if (moveBtn) {
        var moved = false;
        moveBtn.addEventListener('pointerdown', function(e) {
            moved = false;
            moveBtn.setPointerCapture(e.pointerId);
            document.body.classList.add('pet-dragging');
        });
        moveBtn.addEventListener('pointermove', function(e) {
            if (moveBtn.hasPointerCapture(e.pointerId)) {
                moved = true;
                userOffsetX += e.movementX;
                userOffsetY += e.movementY;
                applyTransform();
            }
        });
        moveBtn.addEventListener('pointerup', function(e) {
            document.body.classList.remove('pet-dragging');
            moveBtn.releasePointerCapture(e.pointerId);
            if (moved) sendEnd('pet_move_end');
        });
        moveBtn.addEventListener('dblclick', function() {
            userOffsetX = 0; userOffsetY = 0; applyTransform();
            postMessage({type:'pet_move_reset'});
        });
    }

    var scaleBtn = document.getElementById('pet-scale-btn');
    if (scaleBtn) {
        var moved = false;
        scaleBtn.addEventListener('pointerdown', function(e) {
            moved = false;
            scaleBtn.setPointerCapture(e.pointerId);
            document.body.classList.add('pet-dragging');
        });
        scaleBtn.addEventListener('pointermove', function(e) {
            if (scaleBtn.hasPointerCapture(e.pointerId)) {
                moved = true;
                userScale = Math.max(0.1, userScale * (1 + e.movementX / 200));
                applyTransform();
            }
        });
        scaleBtn.addEventListener('pointerup', function(e) {
            document.body.classList.remove('pet-dragging');
            scaleBtn.releasePointerCapture(e.pointerId);
            if (moved) sendEnd('pet_scale_end');
        });
        scaleBtn.addEventListener('dblclick', function() {
            userScale = 1; applyTransform();
            postMessage({type:'pet_scale_reset'});
        });
    }

    messageBus.on('pet_state', function(msg) {
        userScale = msg.scale;
        userOffsetX = msg.offsetX;
        userOffsetY = msg.offsetY;
        applyTransform();
    });

    postMessage({type:'pet_ready'});
})();
";

    readonly PetBridge bridge;
    readonly StorageSystem storage;
    readonly string stateKey;
    readonly object persistLock = new();
    MoveScaleState state;

    public MoveScaleModule(PetBridge bridge, StorageSystem storage, PetStorageKey storageKey)
    {
        this.bridge = bridge;
        this.storage = storage;
        stateKey = $"{storageKey.Value}/Live2DDeskPet/MoveScale";
        state = storage.GetObject<MoveScaleState>(stateKey) ?? new MoveScaleState();
        bridge.OnMessage += OnBridgeMessage;
    }
    public void Dispose()
    {
        bridge.OnMessage -= OnBridgeMessage;
    }

    void Persist()
    {
        lock (persistLock)
        {
            storage.SetObject(stateKey, state);
        }
    }

    void OnBridgeMessage(string type, JsonElement data)
    {
        switch (type)
        {
            case "pet_ready":
                bridge.SendMessage("pet_state", new { scale = state.Scale, offsetX = state.OffsetX, offsetY = state.OffsetY });
                break;
            case "pet_move_end":
            case "pet_scale_end":
                state.Scale = data.GetProperty("scale").GetDouble();
                state.OffsetX = data.GetProperty("offsetX").GetDouble();
                state.OffsetY = data.GetProperty("offsetY").GetDouble();
                Persist();
                break;
            case "pet_move_reset":
                state.OffsetX = 0;
                state.OffsetY = 0;
                Persist();
                break;
            case "pet_scale_reset":
                state.Scale = 1;
                Persist();
                break;
        }
    }
}
