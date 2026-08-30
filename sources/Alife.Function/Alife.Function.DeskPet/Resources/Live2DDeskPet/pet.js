// ---- Electron IPC 桥 ----
// 窗口以 NodeIntegration=true, ContextIsolation=false 创建，渲染进程可直接使用 require('electron')。
// 懒加载，使后续模块注入的 JS（同样在 node 环境下执行）也能调用 postMessage。
function getIpc() {
    var electron = require('electron');
    return electron.ipcRenderer;
}

// ---- 消息总线 ----
const messageBus = {
    _handlers: {},
    on(type, handler) { (this._handlers[type] ??= []).push(handler); },
    _dispatch(msg) {
        // 统一守卫：非初始化/加载消息且 model 未就绪时拦截
        if (msg.type !== '_init' && msg.type !== 'load' && !model) {
            postLog('warn', '[Pet] Ignore ' + msg.type + ': model not loaded');
            return;
        }
        (this._handlers[msg.type] ?? []).forEach(h => h(msg));
    }
};

// ---- 前端日志转发 ----
function formatLogArg(arg) {
    if (arg instanceof Error) return arg.name + ': ' + arg.message + '\n' + (arg.stack || '');
    if (typeof arg === 'object') {
        try { return JSON.stringify(arg); } catch { return String(arg); }
    }
    return String(arg);
}

function postLog(level) {
    try {
        var args = Array.prototype.slice.call(arguments, 1);
        postMessage({
            type: 'log',
            level: level,
            text: args.map(formatLogArg).join(' ')
        });
    } catch {}
}

{
    var rawWarn = console.warn.bind(console);
    var rawError = console.error.bind(console);
    console.warn = function() {
        postLog.apply(null, ['warn'].concat(Array.prototype.slice.call(arguments)));
        rawWarn.apply(console, arguments);
    };
    console.error = function() {
        postLog.apply(null, ['error'].concat(Array.prototype.slice.call(arguments)));
        rawError.apply(console, arguments);
    };
}

window.addEventListener('error', function(e) {
    var target = e.target;
    if (target && target !== window) {
        postLog('resource-error', target.tagName || 'unknown', target.src || target.href || '');
        return;
    }
    postLog('error', e.message, e.filename + ':' + e.lineno + ':' + e.colno, e.error || '');
}, true);

window.addEventListener('unhandledrejection', function(e) {
    postLog('unhandledrejection', e.reason);
});

// ---- PIXI 应用 ----
const app = new PIXI.Application({
    view: document.getElementById('canvas'),
    autoStart: true,
    resizeTo: window,
    transparent: true,
    backgroundAlpha: 0,
});

let model = null;

// ---- Live2D 模型加载 ----
async function loadModel(url) {
    console.log('[Pet] Loading model:', url);
    if (model) app.stage.removeChild(model);
    try {
        model = await PIXI.live2d.Live2DModel.from(url, {autoInteract: false});
        console.log('[Pet] Model loaded successfully');
    } catch (err) {
        console.error('[Pet] Live2D model load failed:', err);
        postMessage({type: 'loaded'});
        return;
    }
    app.stage.addChild(model);

    var bh = model.internalModel.originalHeight || (model.height / model.scale.y);

    var updateLayout = function() {
        var s = window.innerHeight / 540;
        document.documentElement.style.setProperty('--ui-scale', s);
        var sc = (window.innerHeight * 0.9) / bh;
        model.scale.set(sc);
        model.position.set(window.innerWidth / 2, window.innerHeight / 2);
    };

    model.anchor.set(0.5, 0.5);
    updateLayout();
    model.interactive = true;
    window.addEventListener('resize', updateLayout);

    postMessage({type: 'loaded'});
}

// ---- Electron 通讯 ----
// C# → 渲染进程：监听 'pet' 通道，信封 = { type, ...payload }
getIpc().on('pet', function(event, msg) {
    messageBus._dispatch(msg);
});

// 渲染进程 → C#：发送到 'pet' 通道
function postMessage(data) {
    try { getIpc().send('pet', data); } catch {}
}

// ---- 前端资源注入辅助 ----
function injectCSS(css) {
    var style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
}

function injectHTML(html) {
    var div = document.createElement('div');
    div.innerHTML = html;
    while (div.firstChild) document.body.appendChild(div.firstChild);
}

// ---- 模块内联 UI 与事件（页面独立可用，无需模块注入 HTML） ----

// 气泡
injectCSS('' +
'#bubble-container {' +
    'position:fixed; top:2%; left:50%;' +
    'transform:translateX(-50%) scale(var(--ui-scale));' +
    'transform-origin:top center; width:250px;' +
    'pointer-events:none; z-index:1000;' +
    'opacity:0; transition:opacity 0.3s ease;' +
'}' +
'#bubble-container.show { opacity:1; }' +
'#bubble {' +
    'background:rgba(255,255,255,0.95);' +
    'backdrop-filter:blur(8px); border-radius:18px;' +
    'padding:12px 16px; font-family:\'Microsoft YaHei\',sans-serif;' +
    'font-size:14px; color:#444;' +
    'box-shadow:0 4px 15px rgba(0,0,0,0.1);' +
    'border:1px solid rgba(255,255,255,0.5);' +
    'position:relative; line-height:1.5;' +
'}' +
'#bubble::after {' +
    'content:\'\'; position:absolute; bottom:-8px; left:50%;' +
    'transform:translateX(-50%);' +
    'border-left:8px solid transparent;' +
    'border-right:8px solid transparent;' +
    'border-top:8px solid rgba(255,255,255,0.95);' +
'}' +
'');
injectHTML("<div id='bubble-container'><div id='bubble'></div></div>");

// 思考指示器
injectCSS('' +
'#thinking-indicator {' +
    'position:fixed; top:50px; right:30px;' +
    'display:flex; gap:4px;' +
    'padding:8px 12px;' +
    'background:rgba(255,255,255,0.8);' +
    'backdrop-filter:blur(4px); border-radius:15px;' +
    'box-shadow:0 2px 8px rgba(0,0,0,0.1);' +
    'opacity:0;' +
    'transform:scale(calc(0.8 * var(--ui-scale)));' +
    'transform-origin:top right;' +
    'transition:all 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);' +
    'z-index:1500;' +
'}' +
'#thinking-indicator.show { opacity:1; transform:scale(var(--ui-scale)); }' +
'.dot {' +
    'width:6px; height:6px;' +
    'background-color:#1890ff; border-radius:50%;' +
    'animation:bounce 1.4s infinite ease-in-out both;' +
'}' +
'.dot:nth-child(1) { animation-delay:-0.32s; }' +
'.dot:nth-child(2) { animation-delay:-0.16s; }' +
'@keyframes bounce {' +
    '0%, 80%, 100% { transform:scale(0); }' +
    '40% { transform:scale(1); }' +
'}' +
'');
injectHTML("<div id='thinking-indicator'><div class='dot'></div><div class='dot'></div><div class='dot'></div></div>");

// 聊天输入
injectCSS('' +
'#input-container {' +
    'position:fixed; top:50%; left:50%;' +
    'transform:translate(-50%, -50%) scale(var(--ui-scale));' +
    'transform-origin:center center;' +
    'width:220px; background:rgba(0,0,0,0.4);' +
    'backdrop-filter:blur(10px); border-radius:20px;' +
    'padding:4px 12px; display:flex; align-items:center;' +
    'border:1px solid rgba(255,255,255,0.15);' +
    'z-index:2000;' +
    'box-shadow:0 4px 10px rgba(0,0,0,0.2);' +
    'opacity:0; transition:opacity 0.3s;' +
'}' +
'body:hover #input-container,' +
'#input-container:focus-within { opacity:1; }' +
'#chat-input {' +
    'flex:1; background:transparent; border:none; outline:none;' +
    'color:white; font-size:13px; padding:6px;' +
'}' +
'#chat-input::placeholder { color:rgba(255,255,255,0.5); }' +
'#send-btn {' +
    'background:#ffb7c5; border:none; border-radius:50%;' +
    'width:26px; height:26px; cursor:pointer; margin-left:5px;' +
    'display:flex; align-items:center; justify-content:center;' +
    'color:white;' +
'}' +
'');
injectHTML('' +
"<div id='input-container'>" +
    "<input type='text' id='chat-input' placeholder='和真央聊聊吧喵...' autocomplete='off'>" +
    "<button id='send-btn'>" +
        "<svg viewBox='0 0 24 24' width='14' height='14' fill='currentColor'>" +
            "<path d='M2.01 21L23 12 2.01 3 2 10l15 2-15 2z'/>" +
        "</svg>" +
    "</button>" +
"</div>" +
'');
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

// 缩放手柄
injectCSS('' +
'#resize-btn {' +
    'position:fixed; right:15px; bottom:15px;' +
    'width:28px; height:28px;' +
    'background:rgba(0,0,0,0.4);' +
    'backdrop-filter:blur(10px); border-radius:50%;' +
    'display:flex; justify-content:center; align-items:center;' +
    'color:white; cursor:nwse-resize; z-index:2000;' +
    'box-shadow:0 4px 10px rgba(0,0,0,0.2);' +
    'border:1px solid rgba(255,255,255,0.15);' +
    'opacity:0; transition:opacity 0.3s, transform 0.1s;' +
'}' +
'body:hover #resize-btn { opacity:1; }' +
'#resize-btn:active { transform:scale(0.9); }' +
'');
injectHTML('' +
"<div id='resize-btn'>" +
    "<svg viewBox='0 0 24 24' width='16' height='16' fill='currentColor'>" +
        "<path d='M22 22H2v-2h18V2h2v20z'/>" +
    "</svg>" +
"</div>" +
'');
(function() {
    var btn = document.getElementById('resize-btn');
    var sx, sy;
    btn.addEventListener('pointerdown', function(e) {
        if (e.button !== 0) return;
        btn.setPointerCapture(e.pointerId);
        sx = e.screenX; sy = e.screenY;
    });
    btn.addEventListener('pointermove', function(e) {
        if (btn.hasPointerCapture(e.pointerId)) {
            var dx = e.screenX - sx, dy = e.screenY - sy;
            if (dx !== 0 || dy !== 0) {
                postMessage({type:'resize_delta', dx:dx, dy:dy});
                sx = e.screenX; sy = e.screenY;
            }
        }
    });
    btn.addEventListener('pointerup', function(e) {
        btn.releasePointerCapture(e.pointerId);
    });
})();

// 触摸（poke）与拖拽
window.addEventListener('dblclick', async function(e) {
    if (e.target.tagName !== 'CANVAS') return;
    var areas = await model.hitTest(e.clientX, e.clientY);
    if (areas.length > 0) postMessage({type:'poke', areas:areas});
});
window.addEventListener('mousedown', async function(e) {
    if (e.button !== 0 || e.target.tagName !== 'CANVAS') return;
    var areas = await model.hitTest(e.clientX, e.clientY);
    if (!areas || areas.length === 0) {
        window.petDragging = true;
        postMessage({type:'drag_start'});
    }
});
window.addEventListener('mouseup', function(e) {
    if (window.petDragging === true) {
        window.petDragging = false;
        postMessage({type:'drag_end'});
    }
});

// ---- 核心消息 ----
messageBus.on('_init', function() { postMessage({type: 'ready'}); });
messageBus.on('load', function(msg) { loadModel(msg.url); });

// ---- 模块消息 ----
messageBus.on('bubble', function(msg) {
    document.getElementById('bubble').innerText = msg.text;
    document.getElementById('bubble-container').classList.add('show');
});
messageBus.on('hide-bubble', function() {
    document.getElementById('bubble-container').classList.remove('show');
});
messageBus.on('expression', function(msg) { model.expression(msg.id); });
messageBus.on('motion', function(msg) { model.motion(msg.group, msg.index, PIXI.live2d.MotionPriority.FORCE); });
messageBus.on('look', function(msg) { model.focus(msg.x, msg.y, msg.instant); });
messageBus.on('status', function(msg) {
    if (msg.working) document.getElementById('thinking-indicator').classList.add('show');
    else document.getElementById('thinking-indicator').classList.remove('show');
});