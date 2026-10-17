// 陪玩配置编辑器 - 渲染进程 IPC 桥（nodeIntegration 环境，由插件 IpcMain 提供服务）
const { ipcRenderer } = require('electron');

window.companion = {
  // 配置读写
  getConfig: async () => JSON.parse(await ipcRenderer.invoke('companion:get-config')),
  getScreenSize: async () => JSON.parse(await ipcRenderer.invoke('companion:get-screen-size')),
  collectorTypes: async () => JSON.parse(await ipcRenderer.invoke('companion:collector-types')),
  saveConfig: (config) => ipcRenderer.invoke('companion:save-config', JSON.stringify(config)),
  saveConfigSync: (config) => ipcRenderer.sendSync('companion:save-config-sync', JSON.stringify(config)),
  onConfigPath: (cb) => ipcRenderer.on('companion:config-path', (e, p) => cb(p)),
  openConfigFolder: () => ipcRenderer.invoke('companion:open-config-folder'),
  onConfigSaved: (cb) => ipcRenderer.on('companion:config-saved', cb),

  // 覆盖层控制（主窗口调用）
  showOverlay: (items) => ipcRenderer.invoke('companion:show-overlay', JSON.stringify(items)),
  hideOverlay: () => ipcRenderer.invoke('companion:hide-overlay'),

  // 屏幕取色
  pickColor: () => ipcRenderer.invoke('companion:pick-color'),
  pickAt: (x, y) => ipcRenderer.invoke('companion:pick-at', JSON.stringify({ x, y })),
  pickCancel: () => ipcRenderer.invoke('companion:pick-cancel'),
  pickResult: (hex) => ipcRenderer.invoke('companion:pick-result', hex),
  onPickMode: (cb) => ipcRenderer.on('companion:pick-mode', (e, on) => cb(!!on)),
  onPickResult: (cb) => ipcRenderer.on('companion:pick-result', (e, hex) => cb(hex)),

  // 主窗口监听覆盖层上报的区域变更
  onRegionChanged: (cb) => ipcRenderer.on('companion:region-changed', (e, id, region) => cb(id, typeof region === 'string' ? JSON.parse(region) : region)),

  // 覆盖层接收项目
  onOverlayItems: (cb) => ipcRenderer.on('companion:overlay-items', (e, itemsJson) => cb(JSON.parse(itemsJson))),

  // 覆盖层上报区域变更
  reportRegionChange: (id, region) => ipcRenderer.invoke('companion:region-changed', JSON.stringify({ id, region })),
  finishOverlay: () => ipcRenderer.invoke('companion:hide-overlay'),
  debugSend: (msg) => ipcRenderer.send('companion:overlay-debug', msg),

  // 测试场景（开发验证用）
  openTestScene: () => ipcRenderer.invoke('companion:open-test-scene'),
  getTestSceneBounds: () => ipcRenderer.invoke('companion:get-test-scene-bounds'),

  // 数据浮窗
  onFloatData: (cb) => ipcRenderer.on('companion:float-data', (e, dataJson) => cb(JSON.parse(dataJson))),
  onFloatCharacter: (cb) => ipcRenderer.on('companion:float-character', (e, name) => cb(name)),
  onFloatResetCollapsed: (cb) => ipcRenderer.on('companion:float-reset-collapsed', () => cb()),
  floatExpand: () => ipcRenderer.invoke('companion:float-expand'),
  floatCollapse: () => ipcRenderer.invoke('companion:float-collapse'),
  floatEdit: (gameName) => ipcRenderer.invoke('companion:float-edit', gameName),
  floatMove: (x, y) => ipcRenderer.invoke('companion:float-set-pos', JSON.stringify({ x, y })),
  floatGetPos: async () => JSON.parse(await ipcRenderer.invoke('companion:float-get-pos')),
  floatPause: () => ipcRenderer.invoke('companion:float-pause'),
  floatGames: async () => JSON.parse(await ipcRenderer.invoke('companion:float-games')),
  floatStart: (gameName) => ipcRenderer.invoke('companion:float-start', gameName),
  floatStop: () => ipcRenderer.invoke('companion:float-stop'),
  floatToggle: (gameName, name, enable) => ipcRenderer.invoke('companion:float-toggle', JSON.stringify({ game: gameName, name, enable })),
floatOverlayView: (show, gameName) => ipcRenderer.invoke('companion:float-overlay-view', JSON.stringify({ show, game: gameName || '' })),
  debug: (msg) => ipcRenderer.send('companion:debug', typeof msg === 'string' ? msg : JSON.stringify(msg))
};
