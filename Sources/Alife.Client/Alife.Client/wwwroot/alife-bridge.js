// ---- Alife 通用窗口桥（宿主自动注入，页面无需引用）----
// 适用所有 AlifeWindow（WebView2 宿主）。C# 侧 PostMessageAsync 发出的 JSON
// 会触发 window.alifeWindow.onMessage；页面调用 window.alifeWindow.postMessage
// 会触发 C# 侧 IAlifeWindow.MessageReceived。
(function () {
    if (window.alifeWindow) return;

    const handlers = new Map();

    function postMessage(data) {
        try {
            //WebView2 宿主：字符串原文到达 C# 的 MessageReceived
            window.chrome.webview.postMessage(typeof data === 'string' ? data : JSON.stringify(data));
        } catch {
        }
    }

    window.alifeWindow = {
        postMessage,
        //按 type 订阅宿主消息：onMessage(type, msg)，msg 为解析后的对象（含 type 字段）
        onMessage(type, handler) {
            if (!handlers.has(type)) handlers.set(type, []);
            handlers.get(type).push(handler);
        },
        _dispatch(msg) {
            const list = handlers.get(msg && msg.type) || [];
            list.forEach(handler => {
                try {
                    handler(msg);
                } catch (e) {
                    console.error('[alifeWindow] handler error:', e);
                }
            });
        },
    };

    //宿主（C#）→ 页面：PostMessageAsync 发出的是 JSON 字符串
    try {
        window.chrome.webview.addEventListener('message', function (event) {
            let msg = event.data;
            if (typeof msg === 'string') {
                try {
                    msg = JSON.parse(msg);
                } catch {
                    return;
                }
            }
            window.alifeWindow._dispatch(msg);
        });
    } catch {
    }
})();
