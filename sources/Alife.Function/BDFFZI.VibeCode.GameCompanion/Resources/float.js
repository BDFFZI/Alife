// 陪玩数据浮窗 - 渲染逻辑（JS 拖动：按住移动窗口，轻点触发交互）
const api = window.companion;
const ball = document.getElementById('ball');
const panel = document.getElementById('panel');
const titleEl = document.getElementById('float-title');
const valuesEl = document.getElementById('float-values');
const dotEl = document.getElementById('ball-dot');
const gameSelect = document.getElementById('game-select');
const btnStart = document.getElementById('btn-start');
const btnStop = document.getElementById('btn-stop');
const btnEdit = document.getElementById('btn-edit');
const btnOpenConfig = document.getElementById('btn-open-config');
const btnRefreshConfig = document.getElementById('btn-refresh-config');
const statusEl = document.getElementById('float-status');

function updateStatus() {
  if (!statusEl) return;
  statusEl.className = 'fstatus ' + (running ? 'on' : 'off');
  statusEl.textContent = running ? '运行中' : '未运行';
}

let collapsed = true;
let character = '';
let lastGame = '';
let running = false; // 是否正在陪玩（games 下拉禁用切换）
let localCollectors = []; // 当前所选游戏的采样项（未运行时展示用）
const gameCollectorMap = {}; // 游戏名 → 采样项明细
let refreshing = false;

// 防抖进度动画状态：name → { debounceSecs, updateMs, pending, invalid }
const progressState = {};
let animFrame = null;

// 每帧平滑更新所有行背景：防抖进度从左侧蓝色填充，过期进度从右侧橙色填充（过期在防抖之后才开始）
function animateProgress() {
  const nowMs = Date.now();
  for (const name in progressState) {
    const st = progressState[name];
    const rowEl = valuesEl.querySelector(`[data-name="${CSS.escape(name)}"]`);
    if (!rowEl || !st) continue;
    const dbSecs = st.debounceSecs > 0 ? st.debounceSecs : 0;
    const elapsed = nowMs - st.updateMs;
    let pct = 0;
    if (st.pending && !st.invalid && dbSecs > 0) {
      pct = elapsed / (dbSecs * 1000);
      pct = Math.max(0, Math.min(1, pct));
    }
    // 过期：防抖过后才开始计，进度 = (elapsed - 防抖) / 过期
    let expPct = 0;
    if (st.pending && !st.invalid && st.expireSecs > 0 && dbSecs > 0) {
      expPct = (elapsed - dbSecs * 1000) / (st.expireSecs * 1000);
      expPct = Math.max(0, Math.min(1, expPct));
    }
    rowEl.style.backgroundImage =
      `linear-gradient(to right, rgba(64,150,255,.12), rgba(64,150,255,.12)),` +
      `linear-gradient(to left, rgba(255,140,64,.20), rgba(255,140,64,.20))`;
    rowEl.style.backgroundRepeat = 'no-repeat, no-repeat';
    rowEl.style.backgroundPosition = 'left center, right center';
    rowEl.style.backgroundSize = `${Math.round(pct * 100)}% 100%, ${Math.round(expPct * 100)}% 100%`;
    rowEl.classList.toggle('progress-done', pct >= 1 && st.pending && !st.invalid);
  }
  animFrame = requestAnimationFrame(animateProgress);
}

// 将某行的元数据注册进动画状态
function updateRowProgress(row, it) {
  progressState[it.name] = {
    debounceSecs: it.debounceSecs || 0,
    updateMs: it.updateMs || 0,
    pending: !!it.pending,
    invalid: !!it.invalid,
    expireSecs: it.expireSecs || 0
  };
  if (!animFrame) animFrame = requestAnimationFrame(animateProgress);
}

function updatePanelTitle() {
  // 标题只表示身份；运行状态由头部状态徽标（运行中/未运行）单独显示
  titleEl.textContent = character ? `${character}·${lastGame}` : (lastGame || '陪玩数据');
}

async function init() {
  api.onFloatData(render);
  api.onFloatCharacter((name) => { character = name || ''; updatePanelTitle(); });
  // 后端复位为收起小球（启动/重载时）：同步前端收起状态，避免透明矩形残留挡鼠标
  api.onFloatResetCollapsed(() => collapse());
  // 编辑器保存后立即刷新采样项清单
  api.onConfigSaved(() => refreshCollectors());
  // 球：JS 拖动移动窗口，轻点展开（原生 drag 会显示方形背景，故用 JS）
  setupDrag(ball, expand);
  // 面板头：原生 -webkit-app-region: drag 拖动；标题点击收起（与球同位置，两下收展）
  titleEl.addEventListener('click', collapse);

  await loadGames();
  btnStart.addEventListener('click', startGame);
  btnStop.addEventListener('click', stopGame);
  btnEdit.addEventListener('click', () => api.floatEdit(gameSelect.value));
  btnOpenConfig.addEventListener('click', () => api.openConfigFolder());
  btnRefreshConfig.addEventListener('click', refreshCollectors);
  const overlayCb = document.getElementById('btn-overlay-view');
  overlayCb.addEventListener('change', () => api.floatOverlayView(overlayCb.checked, gameSelect.value));
  applyState();

  // 编辑器关闭保存后刷新下拉列表和采样项（不再靠定时轮询）
  api.onConfigSaved(() => refreshCollectors());
}

// 重新拉取所选游戏的最新采样项清单（未运行时刷新展示，运行中交给快照）
async function refreshCollectors() {
  if (refreshing) return;
  refreshing = true;
  try {
    const games = await api.floatGames();
    // 同步下拉框选项（支持新增/删除游戏后自动更新）
    const currentVal = gameSelect.value;
    gameSelect.innerHTML = '';
    (games || []).forEach((g) => {
      const opt = document.createElement('option');
      opt.value = g.name;
      opt.textContent = g.name;
      gameSelect.appendChild(opt);
      gameCollectorMap[g.name] = g.collectors || [];
    });
    if (games && games.length > 0) {
      // 保持当前选中的游戏，若已不存在则选第一个
      const exists = games.some(g => g.name === currentVal);
      gameSelect.value = exists ? currentVal : games[0].name;
      localStorage.setItem('companionLastGame', gameSelect.value);
    }
    refreshLocalCollectors();
    if (!running) render();
  } catch (e) {
    console.error('刷新采样项清单失败', e);
  } finally {
    refreshing = false;
  }
}

async function loadGames() {
  try {
    const games = await api.floatGames();
    const savedGame = localStorage.getItem('companionLastGame') || '';
    gameSelect.innerHTML = '';
    (games || []).forEach((g) => {
      const opt = document.createElement('option');
      opt.value = g.name;
      opt.textContent = g.name;
      gameSelect.appendChild(opt);
      gameCollectorMap[g.name] = g.collectors || [];
    });
    if (games && games.length > 0) {
      // 恢复上次选中的游戏，若已不存在则选第一个
      const savedExists = games.some(g => g.name === savedGame);
      gameSelect.value = savedExists ? savedGame : games[0].name;
    }
    gameSelect.addEventListener('change', () => {
      localStorage.setItem('companionLastGame', gameSelect.value);
      refreshLocalCollectors();
      updatePanelTitle();
      render();
    });
    refreshLocalCollectors();
    updatePanelTitle();
    updateStatus();
    refreshRunningState();
    // 首次进入即渲染所选游戏的采样项列表
    render();
  } catch (e) {
    console.error('加载游戏列表失败', e);
  }
}

function refreshLocalCollectors() {
  const g = gameSelect.value;
  if (g) lastGame = g;
  localCollectors = gameCollectorMap[g] || [];
}

function startGame() {
  const name = gameSelect.value;
  if (!name) {
    showFloatHint('尚未配置任何游戏，请先「编辑陪玩配置」新建');
    return;
  }
  const collectors = gameCollectorMap[name] || [];
  const enabledCount = collectors.filter((c) => c.enabled !== false).length;
  if (collectors.length === 0 || enabledCount === 0) {
    showFloatHint('该游戏没有已启用的采集目标，请先「编辑陪玩配置」添加');
    api.floatExpand(); // 确保面板展开可见提示
    return;
  }
  api.floatStart(name);
}

// 在浮窗内临时显示一条提示（几秒后自动消失）
let hintTimer = null;
function showFloatHint(text) {
  const statusEl = document.getElementById('float-status');
  if (!statusEl) return;
  statusEl.textContent = text;
  statusEl.className = 'fstatus warn';
  if (hintTimer) clearTimeout(hintTimer);
  hintTimer = setTimeout(() => {
    updateStatus();
    hintTimer = null;
  }, 3500);
}

function stopGame() {
  if (!running) return; // 未在陪玩，忽略（避免误发给 AI）
  api.floatStop();
}

function refreshRunningState() {
  // 无任何游戏可选：开启/关闭均禁用（避免误发给 AI）
  const hasGame = !!gameSelect.value && gameCollectorMap[gameSelect.value] !== undefined;
  // 陪玩中：下拉禁用（不可切换游戏），可关闭；否则可按需开启
  gameSelect.disabled = running;
  btnStart.disabled = running || !hasGame;
  btnStop.disabled = !running;
}

// JS 拖动：mousedown 时从主进程获取窗口基准位置(GetPosition，DIP)，
// mousemove 用「基准 + 鼠标位移」绝对坐标调用 SetPosition（仅移动，不带宽高），
// 无位移的放开视为点击触发 clickAction
function setupDrag(el, clickAction) {
  let dragging = false;
  let moved = false;
  let baseX = 0, baseY = 0, sx = 0, sy = 0;

  el.addEventListener('mousedown', async (e) => {
    if (e.target.closest('button, input, select')) return;
    dragging = true;
    moved = false;
    sx = e.screenX;
    sy = e.screenY;
    e.preventDefault();
    try {
      const base = await api.floatGetPos();
      baseX = base.x;
      baseY = base.y;
    } catch (err) {
      baseX = window.screenX;
      baseY = window.screenY;
    }
  });

  window.addEventListener('mousemove', (e) => {
    if (!dragging) return;
    const dx = e.screenX - sx;
    const dy = e.screenY - sy;
    if (Math.abs(dx) + Math.abs(dy) > 3) moved = true;
    if (moved) {
      api.floatMove(baseX + dx, baseY + dy);
    }
  });

  window.addEventListener('mouseup', () => {
    if (!dragging) return;
    const wasClick = !moved;
    dragging = false;
    if (wasClick && clickAction) clickAction();
  });
}

function render(data) {
  try {
    const game = data?.GameName || '';
    if (game) lastGame = game;
    // 只有真实快照且 State==1（Running）才算运行中；无数据（未运行/未推送）一律视为未运行
    running = data != null && data.State === 1;
    updatePanelTitle();
    updateStatus();
    dotEl.classList.toggle('on', running);
    refreshRunningState();

    const enabled = data?.Enabled;
    const hasSnapshot = data != null && (running || (enabled != null && Object.keys(enabled).length > 0));
    const items = hasSnapshot ? itemsFromSnapshot(data) : itemsFromConfig();

    valuesEl.innerHTML = '';
    // 清空旧动画状态，避免残留项
    for (const k in progressState) delete progressState[k];
    if (items.length === 0) {
      valuesEl.appendChild(makeEmpty('暂无采样项，请先配置采集目标'));
      return;
    }
    items.forEach((it) => valuesEl.appendChild(rowFor(it)));
  } catch (e) {
    console.error('[float] render 异常', e);
  }
}

// 运行/已停时的数据源：禁用项也会列出（含启用状态、三值、可信度）
function itemsFromSnapshot(data) {
  const values = data.Values || {};
  const raws = data.DebugValues || {};
  const currents = data.CurrentValues || {};
  const pusheds = data.PushedValues || {};
  const debounceSecs = data.DebounceSeconds || {};
  const updateTicks = data.LastUpdateTimeTicks || {};
  const expireSecs = data.ExpireSeconds || {};
  const enabled = data.Enabled || {};
  let names = Object.keys(enabled);
  if (names.length === 0) names = Object.keys(values);
  return names.map((name) => {
    const enabledValue = enabled[name] !== false;
    const pending = currents[name] != null && currents[name] !== pusheds[name];
    return {
      name,
      enabled: enabledValue,
      raw: raws[name] == null ? null : raws[name],
      current: currents[name] == null ? null : currents[name],
      pushed: pusheds[name] == null ? null : pusheds[name],
      pending,
      debounceSecs: typeof debounceSecs[name] === 'number' ? debounceSecs[name] : 0,
      updateMs: typeof updateTicks[name] === 'number' ? (updateTicks[name] - 621355968000000000) / 10000 : 0,
      expireSecs: typeof expireSecs[name] === 'number' ? expireSecs[name] : 0,
      invalid: !enabledValue
    };
  });
}

// 未运行时的数据源：按所选游戏配置列出采样项（无数据，改动写入配置下次启动生效）
function itemsFromConfig() {
  refreshLocalCollectors();
  return localCollectors.map((c) => ({
    name: c.name,
    enabled: c.enabled !== false,
    raw: null,
    current: null,
    pushed: null,
    pending: false,
    debounceSecs: 0,
    updateMs: 0,
    expireSecs: 0,
    invalid: false
  }));
}

// 统一的行渲染（默认态 / 运行态复用同一套）
function rowFor(it) {
  const row = document.createElement('div');
  row.className = 'val-row' + (it.invalid ? ' invalid' : '');
  row.dataset.name = it.name;
  // 初始背景状态（动画循环会平滑更新）
  updateRowProgress(row, it);
  const top = document.createElement('div');
  top.className = 'val-top';

  // 快速开关
  const cb = document.createElement('input');
  cb.type = 'checkbox';
  cb.className = 's-enable';
  cb.checked = it.enabled;
  cb.title = '启用/禁用该采样项';
  cb.addEventListener('change', () => {
    if (lastGame) api.floatToggle(lastGame, it.name, cb.checked);
    else cb.checked = !cb.checked;
  });

  const n = document.createElement('span');
  n.className = 'val-name';
  n.textContent = it.name;

  // 三值显示：DebugValue | CurrentValue | PushedValue
  const rawEl = document.createElement('span');
  rawEl.className = 'val-raw';
  rawEl.textContent = it.raw == null ? '—' : it.raw;
  rawEl.title = '调试值';

  const curEl = document.createElement('span');
  curEl.className = 'val-cur';
  curEl.textContent = it.current == null ? '—' : it.current;
  curEl.title = '待推送值';

  const pushEl = document.createElement('span');
  pushEl.className = 'val-push';
  pushEl.textContent = it.pushed == null ? '—' : it.pushed;
  pushEl.title = '已推送值';

  top.append(cb, n, rawEl, curEl, pushEl);
  row.appendChild(top);
  return row;
}

function makeEmpty(text) {
  const empty = document.createElement('div');
  empty.className = 'empty';
  empty.textContent = text;
  return empty;
}

function expand() {
  if (!collapsed) return;
  collapsed = false;
  panel.classList.add('show');
  ball.classList.add('ball-hidden');
  api.floatExpand();
  refreshCollectors(); // 展开时同步编辑器改动的新增/删除采样项
}

function collapse() {
  if (collapsed) return;
  collapsed = true;
  panel.classList.remove('show');
  ball.classList.remove('ball-hidden');
  api.floatCollapse();
}

function applyState() {
  if (collapsed) {
    panel.classList.remove('show');
    ball.classList.remove('ball-hidden');
  } else {
    panel.classList.add('show');
    ball.classList.add('ball-hidden');
  }
}

init();
window.addEventListener('error', (e) => {
  console.error('[float] 全局错误', e.message, e.error && e.error.stack);
  try { api.debug({ err: e.message, stack: e.error && e.error.stack }); } catch (err) {}
});