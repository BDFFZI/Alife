// 陪玩窗口 - 渲染进程桥（基于 Alife.Client 的 window.alifeWindow 通用消息通道）
// 与主进程约定：
//   request  { kind:'request', id, channel, payload }
//   response { kind:'response', id, result, error }
//   event    { kind:'event', channel, payload }
(function () {
  const host = window.alifeWindow;
  const pending = new Map();
  const listeners = Object.create(null);
  const latest = Object.create(null);
  let seq = 0;

  function post(message) {
    if (!host) return;
    try { host.postMessage(message); } catch (e) { console.error('[companion] postMessage failed', e); }
  }

  function subscribe(channel, handler) {
    (listeners[channel] || (listeners[channel] = [])).push(handler);
    // 页面脚本注册监听通常晚于窗口就绪消息，回放最近一次事件避免丢失初始状态
    if (channel in latest) {
      try { handler(latest[channel]); } catch (e) { console.error('[companion] handler error', channel, e); }
    }
  }

  function emit(channel, payload) {
    latest[channel] = payload;
    const handlers = listeners[channel];
    if (!handlers) return;
    handlers.forEach((handler) => {
      try { handler(payload); } catch (e) { console.error('[companion] handler error', channel, e); }
    });
  }

  function invoke(channel, payload) {
    if (!host) return Promise.reject(new Error('alifeWindow bridge unavailable'));
    return new Promise((resolve, reject) => {
      const id = ++seq;
      pending.set(id, { resolve, reject });
      post({ kind: 'request', id, channel, payload });
    });
  }

  function send(channel, payload) {
    post({ kind: 'request', id: 0, channel, payload });
  }

  if (host) {
    host.onMessage(function (message) {
      if (!message) return;
      if (typeof message === 'string') {
        try { message = JSON.parse(message); } catch { return; }
      }
      if (message.kind === 'response') {
        const task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id);
        if (message.error) task.reject(new Error(message.error));
        else task.resolve(message.result);
      } else if (message.kind === 'event') {
        emit(message.channel, message.payload);
      }
    });
  }

  window.companion = {
    closeWindow: () => send('companion:close-window'),

    // 配置读写
    getConfig: () => invoke('companion:get-config'),
    getScreenSize: () => invoke('companion:get-screen-size'),
    collectorTypes: () => invoke('companion:collector-types'),
    saveConfig: (config) => invoke('companion:save-config', config),
    saveConfigSync: (config) => send('companion:save-config-sync', config),
    onConfigPath: (cb) => subscribe('companion:config-path', (p) => cb(p)),
    openConfigFolder: () => invoke('companion:open-config-folder'),
    onConfigSaved: (cb) => subscribe('companion:config-saved', () => cb()),

    // 覆盖层控制（主窗口调用）
    showOverlay: (items) => invoke('companion:show-overlay', items),
    hideOverlay: () => invoke('companion:hide-overlay'),

    // 屏幕取色
    pickColor: () => invoke('companion:pick-color'),
    pickAt: (x, y) => invoke('companion:pick-at', { x, y }),
    pickCancel: () => invoke('companion:pick-cancel'),
    pickResult: (hex) => invoke('companion:pick-result', hex),
    onPickMode: (cb) => subscribe('companion:pick-mode', (on) => cb(!!on)),
    onPickResult: (cb) => subscribe('companion:pick-result', (hex) => cb(hex)),

    // 主窗口监听覆盖层上报的区域变更
    onRegionChanged: (cb) => subscribe('companion:region-changed', (p) => cb(p && p.id, p && p.region)),

    // 覆盖层接收项目
    onOverlayItems: (cb) => subscribe('companion:overlay-items', (items) => cb(items)),

    // 覆盖层上报区域变更
    reportRegionChange: (id, region) => invoke('companion:region-changed', { id, region }),
    finishOverlay: () => invoke('companion:hide-overlay'),
    debugSend: (msg) => send('companion:overlay-debug', msg),

    // 测试场景（开发验证用）
    openTestScene: () => invoke('companion:open-test-scene'),
    getTestSceneBounds: () => invoke('companion:get-test-scene-bounds'),

    // 数据浮窗
    onFloatData: (cb) => subscribe('companion:float-data', (data) => cb(data)),
    onFloatCharacter: (cb) => subscribe('companion:float-character', (name) => cb(name)),
    onFloatResetCollapsed: (cb) => subscribe('companion:float-reset-collapsed', () => cb()),
    floatExpand: () => invoke('companion:float-expand'),
    floatCollapse: () => invoke('companion:float-collapse'),
    floatEdit: (gameName) => invoke('companion:float-edit', gameName),
    floatMove: (x, y) => invoke('companion:float-set-pos', { x, y }),
    floatGetPos: () => invoke('companion:float-get-pos'),
    floatPause: () => invoke('companion:float-pause'),
    floatGames: () => invoke('companion:float-games'),
    floatStart: (gameName) => invoke('companion:float-start', gameName),
    floatStop: () => invoke('companion:float-stop'),
    floatToggle: (gameName, name, enable) => invoke('companion:float-toggle', { game: gameName, name, enable }),
    floatOverlayView: (show, gameName) => invoke('companion:float-overlay-view', { show, game: gameName || '' }),
    debug: (msg) => send('companion:debug', typeof msg === 'string' ? msg : JSON.stringify(msg))
  };

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', () => post({ kind: 'ready' }));
  else
    setTimeout(() => post({ kind: 'ready' }), 0);
})();
