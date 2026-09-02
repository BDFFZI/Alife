// ---- Electron 通讯 ----

// 每个桌宠实例使用独立的 IPC 频道，避免多开时消息互相串扰
var petChannel = new URLSearchParams(location.search).get('petChannel') || 'pet';

function getIpc() {
    var electron = require('electron');
    return electron.ipcRenderer;
}

function postMessage(data) {
    try {
        getIpc().send(petChannel, JSON.stringify(data));
    } catch {
    }
}

const messageBus = {
    _handlers: new Map(),
    on(type, handler) {
        if (!this._handlers.has(type)) {
            this._handlers.set(type, []);
        }

        this._handlers.get(type).push(handler);
    },
    _dispatch(msg) {
        const handlers = this._handlers.get(msg.type) || [];

        handlers.forEach(handler => handler(msg));
    }
};

getIpc().on(petChannel, function (event, msg) {
    if (typeof msg === 'string') {
        try {
            msg = JSON.parse(msg);
        } catch {
            return;
        }
    }
    messageBus._dispatch(msg);
});


// ---- 前端日志转发 ----
function formatLogArg(arg) {
    if (arg instanceof Error) return arg.name + ': ' + arg.message + '\n' + (arg.stack || '');
    if (typeof arg === 'object') {
        try {
            return JSON.stringify(arg);
        } catch {
            return String(arg);
        }
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
    } catch {
    }
}

{
    var rawWarn = console.warn.bind(console);
    var rawError = console.error.bind(console);
    console.warn = function () {
        postLog.apply(null, ['warn'].concat(Array.prototype.slice.call(arguments)));
        rawWarn.apply(console, arguments);
    };
    console.error = function () {
        postLog.apply(null, ['error'].concat(Array.prototype.slice.call(arguments)));
        rawError.apply(console, arguments);
    };
}

window.addEventListener('error', function (e) {
    var target = e.target;
    if (target && target !== window) {
        postLog('resource-error', target.tagName || 'unknown', target.src || target.href || '');
        return;
    }
    postLog('error', e.message, e.filename + ':' + e.lineno + ':' + e.colno, e.error || '');
}, true);

window.addEventListener('unhandledrejection', function (e) {
    postLog('unhandledrejection', e.reason);
});

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

// ---- 加载模型 ----

messageBus.on('load', function (msg) {
    loadModel(msg.url);
});

const app = new PIXI.Application({
    view: document.getElementById('canvas'),
    autoStart: true,
    resizeTo: window,
    transparent: true,
    backgroundAlpha: 0,
});

let model = null;

// 用户在模型上的追加偏移与缩放（独立于布局基准，窗口 resize/updateLayout 不会重置）
let userScale = 1;
let userOffsetX = 0;
let userOffsetY = 0;

// 将布局基准 + 用户追加变换应用到模型
function applyTransform() {
    if (!model) return;
    const sc = window.innerHeight / model.internalModel.originalHeight;
    model.scale.set(sc * userScale);
    model.position.set(window.innerWidth * 0.5 + userOffsetX, window.innerHeight * 0.48 + userOffsetY);
}

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
    
    var updateLayout = function () {
        var s = window.innerHeight / 540;
        document.documentElement.style.setProperty('--ui-scale', s);
        applyTransform();
    };

    model.anchor.set(0.5, 0.5);
    updateLayout();
    model.interactive = true;
    window.addEventListener('resize', updateLayout);

    postMessage({type: 'loaded'});
}

// ---- 初始化完成 ----
postMessage({type: 'init'});