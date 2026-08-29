// 陪玩配置编辑器 - 主窗口逻辑（集成版，IPC 由插件提供）
// 结构：先按游戏选 → 下方显示该游戏的「多个数据采集目标」
// 每个采集目标 = 数据名 + 采集类型 + 类型特定数据（语音=指定文本 / 文本=Rect范围 / 像素=点位+颜色值与代称）
// 对局上报：全部采样项数据有效且有数值变动时给 AI 发新值
const api = window.companion;

let config = { Games: [] };
let selectedIndex = -1;
let dragIndex = null;
let pickTarget = null; // 屏幕取色目标 {cfg, key, render}

// 采样器类型列表，从注册器动态拉取
let COLLECTOR_TYPES = [];
let screenSize = { width: 0, height: 0 }; // 当前物理分辨率

document.addEventListener('DOMContentLoaded', init);

async function init() {
  config = await api.getConfig();
  COLLECTOR_TYPES = await api.collectorTypes();
  try { screenSize = await api.getScreenSize(); } catch (e) { console.error('获取屏幕分辨率失败', e); }
  api.onConfigPath((p) => {
    document.getElementById('config-path').textContent = p || '';
  });
  api.onRegionChanged((id, region) => {
    applyRegion(id, region);
  });

  // 屏幕取色结果：写入当前取色目标
  api.onPickResult((hex) => {
    if (pickTarget) {
      pickTarget.cfg[pickTarget.key] = hex;
      pickTarget.render();
      pickTarget = null;
    }
    renderGameEditor();
  });

  bindEvents();
  if (config.Games.length > 0) {
    // 优先选中浮窗传入的 DefaultGame；不存在则选第一个
    const defIdx = config.DefaultGame ? config.Games.findIndex(g => g.GameName === config.DefaultGame) : -1;
    selectedIndex = defIdx >= 0 ? defIdx : 0;
  }
  render();
}

function bindEvents() {
  document.getElementById('btn-save-config').addEventListener('click', saveConfigNow);
  document.getElementById('btn-convert-res').addEventListener('click', convertResolution);
  document.getElementById('btn-add-game').addEventListener('click', () => promptGameName('新建游戏', '', addGame));
  document.getElementById('btn-rename-game').addEventListener('click', () => {
    const g = currentGame();
    if (!g) return;
    promptGameName('重命名游戏', g.GameName, renameGame);
  });
  document.getElementById('btn-del-game').addEventListener('click', () => {
    const g = currentGame();
    if (!g) return;
    promptConfirm(`确定删除游戏「${g.GameName || '未命名'}」吗？`, deleteGame);
  });
  document.getElementById('game-select').addEventListener('change', (e) => {
    selectedIndex = parseInt(e.target.value, 10);
    render();
  });
  document.getElementById('btn-add-target').addEventListener('click', addTarget);
  document.getElementById('btn-name-cancel').addEventListener('click', hideNameModal);
  document.getElementById('btn-name-ok').addEventListener('click', submitNameModal);
  document.getElementById('name-input').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') submitNameModal();
    if (e.key === 'Escape') hideNameModal();
  });
  document.getElementById('btn-confirm-cancel').addEventListener('click', hideConfirmModal);
  document.getElementById('btn-confirm-ok').addEventListener('click', submitConfirmModal);

  // 关闭窗口时同步落盘
  window.addEventListener('beforeunload', () => {
    try {
      api.saveConfigSync(config);
    } catch (e) { console.error('关闭前保存失败', e); }
  });
}

// ---------- 游戏名字弹窗 ----------
let nameModalMode = 'new';   // 'new' | 'rename'
let nameModalCallback = null;

function promptGameName(title, initial, callback) {
  nameModalMode = callback === addGame ? 'new' : 'rename';
  nameModalCallback = callback;
  document.getElementById('name-modal-title').textContent = title;
  const input = document.getElementById('name-input');
  input.value = initial || '';
  document.getElementById('name-modal').style.display = 'flex';
  input.focus();
  input.select();
}

function hideNameModal() {
  document.getElementById('name-modal').style.display = 'none';
  nameModalCallback = null;
}

function submitNameModal() {
  const name = document.getElementById('name-input').value.trim();
  if (!name) return;
  const cb = nameModalCallback;
  hideNameModal();
  if (cb) cb(name);
}

// ---------- 确认弹窗 ----------
let confirmCallback = null;

function promptConfirm(text, callback) {
  confirmCallback = callback;
  document.getElementById('confirm-text').textContent = text;
  document.getElementById('confirm-modal').style.display = 'flex';
}

function hideConfirmModal() {
  document.getElementById('confirm-modal').style.display = 'none';
  confirmCallback = null;
}

function submitConfirmModal() {
  const cb = confirmCallback;
  hideConfirmModal();
  if (cb) cb();
}

function currentGame() {
  if (selectedIndex < 0 || !config.Games[selectedIndex]) return null;
  return config.Games[selectedIndex];
}

function render() {
  renderGameSelect();
  renderGameEditor();
}

function renderGameSelect() {
  const sel = document.getElementById('game-select');
  sel.innerHTML = '';
  config.Games.forEach((g, i) => {
    const opt = document.createElement('option');
    opt.value = i;
    opt.textContent = g.GameName || `（未命名游戏 ${i + 1}）`;
    opt.selected = i === selectedIndex;
    sel.appendChild(opt);
  });
}

function addGame(name) {
  config.Games.push({
    GameName: name,
    Collectors: [],
    BaseScreenWidth: screenSize.width || 0,
    BaseScreenHeight: screenSize.height || 0
  });
  selectedIndex = config.Games.length - 1;
  render();
}

function renameGame(name) {
  const g = currentGame();
  if (!g) return;
  g.GameName = name;
  renderGameSelect();
  
}

function deleteGame() {
  if (selectedIndex < 0) return;
  config.Games.splice(selectedIndex, 1);
  if (selectedIndex >= config.Games.length) selectedIndex = config.Games.length - 1;
  render();
  
}

// ---------- 数据采集目标 ----------
function renderGameEditor() {
  const g = currentGame();
  const body = document.getElementById('game-editor');
  const placeholder = document.getElementById('no-selection');
  if (!g) {
    body.style.display = 'none';
    placeholder.style.display = 'block';
    updateResConvertButton(null);
    return;
  }
  placeholder.style.display = 'none';
  body.style.display = 'block';

  updateResConvertButton(g);
  renderTargetList(g);
}

// 读取用户自定义的目标分辨率；两个输入框均填写时优先使用，否则用当前物理分辨率
function targetRes() {
  const w = parseInt(document.getElementById('res-target-w').value, 10);
  const h = parseInt(document.getElementById('res-target-h').value, 10);
  if (w > 0 && h > 0) return { width: w, height: h, custom: true };
  return { width: screenSize.width, height: screenSize.height, custom: false };
}

// 依据当前游戏配置的基准分辨率与当前物理分辨率，展示分辨率信息条并显隐「转换分辨率」按钮
function updateResConvertButton(g) {
  const btn = document.getElementById('btn-convert-res');
  const info = document.getElementById('res-info');
  const baseEl = document.getElementById('res-base');
  const curEl = document.getElementById('res-current');
  const stateEl = document.getElementById('res-state');
  const curOk = screenSize.width > 0 && screenSize.height > 0;

  const showInfo = (baseW, baseH) => {
    if (info) info.style.display = '';
    if (baseEl) baseEl.textContent = baseW > 0 ? `${baseW}×${baseH}` : '未记录';
    if (curEl) curEl.textContent = curOk ? `${screenSize.width}×${screenSize.height}` : '—';
  };
  const hideInfo = () => { if (info) info.style.display = 'none'; };

  // 输入框变化时刷新按钮文案
  const sync = () => { if (g) updateResConvertButton(g); };
  const wIn = document.getElementById('res-target-w');
  const hIn = document.getElementById('res-target-h');
  if (wIn) wIn.oninput = sync;
  if (hIn) hIn.oninput = sync;

  if (!g || !curOk) {
    if (btn) btn.style.display = 'none';
    hideInfo();
    return;
  }
  const baseW = g.BaseScreenWidth, baseH = g.BaseScreenHeight;
  showInfo(baseW, baseH);

  const tgt = targetRes();

  if (baseW > 0 && baseH > 0) {
    if (baseW === tgt.width && baseH === tgt.height) {
      if (btn) btn.style.display = 'none';
      if (stateEl) { stateEl.textContent = tgt.custom ? '已自定义' : '一致'; stateEl.className = 'res-state ok'; }
    } else {
      if (btn) {
        btn.style.display = '';
        btn.textContent = `转换分辨率 ${baseW}×${baseH} → ${tgt.width}×${tgt.height}`;
        btn.title = tgt.custom
          ? '按比例缩放所有采集区域到自定义目标分辨率，并更新基准分辨率'
          : '按比例缩放所有采集区域到当前分辨率，并更新基准分辨率';
      }
      if (stateEl) { stateEl.textContent = tgt.custom ? '自定义' : '不一致'; stateEl.className = 'res-state bad'; }
    }
  } else {
    // 基准未记录（老配置）：以目标分辨率为基准记录
    if (btn) {
      btn.style.display = '';
      btn.textContent = `记录基准分辨率 ${tgt.width}×${tgt.height}`;
      btn.title = tgt.custom
        ? '本游戏尚未记录基准分辨率，点击以自定义目标分辨率作为基准'
        : '本游戏尚未记录基准分辨率，点击以当前分辨率作为基准（后续分辨率变化时可一键转换）';
    }
    if (stateEl) { stateEl.textContent = '未记录基准'; stateEl.className = 'res-state warn'; }
  }
}

function fmtRegion(r, kind) {
  if (!r) return '未设置';
  if (kind === 'point') {
    return `点 (${r.X},${r.Y})`;
  }
  if (kind === 'triangle') {
    const tri = r.Triangle || [];
    if (tri.length === 0) return '未拖拽（点击拖拽编辑）';
    return '三角 ' + tri.map((p) => `(${p.X},${p.Y})`).join(' ');
  }
  if (kind === 'sector') {
    return `扇面 圆心(${r.X},${r.Y}) 半径${r.Radius || 0} ${r.StartAngle || 0}°~${(r.StartAngle || 0) + (r.SweepAngle || 0)}°`;
  }
  return `${r.X}, ${r.Y}  ${r.Width}×${r.Height}`;
}

function validatePrerequisite(input, currentCollector, game) {
  const val = (input.value || '').trim();
  if (!val) {
    input.classList.remove('s-input-error');
    input.title = '仅当前置采样器有效时才执行更新和推送（填写采样器名称，留空无前置）';
    return;
  }
  const exists = (game.Collectors || []).some(c => c !== currentCollector && c.Name === val);
  if (exists) {
    input.classList.remove('s-input-error');
    input.title = '前置采样器: ' + val;
  } else {
    input.classList.add('s-input-error');
    input.title = '未找到名为「' + val + '」的采样器';
  }
}

function renderTargetList(g) {
  const list = document.getElementById('target-list');
  list.innerHTML = '';

  if (!g.Collectors || g.Collectors.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'hint';
    empty.textContent = '暂无采集目标，点击「+ 添加采集目标」创建。';
    list.appendChild(empty);
    return;
  }

  g.Collectors.forEach((t, i) => {
    const firstType = (COLLECTOR_TYPES[0] && COLLECTOR_TYPES[0].type) || 'text';
    if (!t.Config) t.Config = {};
    const cfg = t.Config;
    const card = document.createElement('div');
    card.className = 'target-card';

    // 拖拽排序只在手柄上触发，卡片本身不 draggable（保证文本框可框选）
    card.addEventListener('dragover', (e) => {
      e.preventDefault();
      card.classList.add('drag-over');
    });
    card.addEventListener('dragleave', () => {
      card.classList.remove('drag-over');
    });
    card.addEventListener('drop', (e) => {
      e.preventDefault();
      card.classList.remove('drag-over');
      if (dragIndex !== null && dragIndex !== i) {
        const [moved] = g.Collectors.splice(dragIndex, 1);
        const targetIdx = i > dragIndex ? i - 1 : i;
        g.Collectors.splice(targetIdx, 0, moved);
        renderTargetList(g);
      }
      dragIndex = null;
    });

    // 数据名 + 采集类型 + 删除
    const top = document.createElement('div');
    top.className = 'target-top';
    const handle = document.createElement('span');
    handle.className = 't-drag-handle';
    handle.textContent = '⠿';
    handle.title = '拖动此手柄排序';
    handle.draggable = true;
    handle.addEventListener('dragstart', (e) => {
      dragIndex = i;
      card.classList.add('dragging');
      e.stopPropagation();
    });
    handle.addEventListener('dragend', () => {
      card.classList.remove('dragging');
      card.classList.remove('drag-over');
      list.querySelectorAll('.target-card').forEach((el) => el.classList.remove('drag-over'));
      dragIndex = null;
    });
    const nameLbl = document.createElement('label');
    nameLbl.textContent = '数据名';
    const nameInput = document.createElement('input');
    nameInput.className = 't-name';
    nameInput.type = 'text';
    nameInput.placeholder = '如：生命值 / 击杀提示';
    nameInput.value = t.Name || '';
    nameInput.addEventListener('input', () => { t.Name = nameInput.value;  });

    // 陪玩验证器（与名称同行）
    const vLbl = document.createElement('label');
    vLbl.textContent = '陪玩验证器';
    const vCb = document.createElement('input');
    vCb.type = 'checkbox';
    vCb.className = 's-base-cb';
    vCb.checked = t.IsValidator === true;
    vCb.title = '作为陪玩验证器：其 CurrentValue 非空（有数据）才允许推送';
    vCb.addEventListener('change', () => { t.IsValidator = vCb.checked; });

    // 复制、删除按钮
    const del = document.createElement('button');
    del.className = 'small danger t-del';
    del.textContent = '删除';
    del.addEventListener('click', () => { g.Collectors.splice(i, 1); renderTargetList(g);  });
    const copy = document.createElement('button');
    copy.className = 'small t-copy';
    copy.textContent = '复制';
    copy.title = '复制此采样器（含全部配置）';
    copy.addEventListener('click', () => {
      const clone = JSON.parse(JSON.stringify(t));
      clone.Name = (clone.Name || `目标${i + 1}`) + ' 副本';
      g.Collectors.splice(i + 1, 0, clone);
      renderTargetList(g);
    });

    // 第二行：防抖(s) / 过期(s) / 强制推送
    const row2 = document.createElement('div');
    row2.className = 'sys-row';
    const dBtn = document.createElement('label');
    dBtn.textContent = '防抖(s)';
    dBtn.title = '本采样器产出新值后至少等待该时长才推送（0=立即）';
    const dInput = document.createElement('input');
    dInput.type = 'number';
    dInput.min = 0;
    dInput.step = 0.1;
    dInput.className = 's-base-db';
    if (t.DebounceSeconds == null) t.DebounceSeconds = 0.6;
    dInput.value = t.DebounceSeconds;
    dInput.addEventListener('input', () => {
      const n = parseFloat(dInput.value);
      t.DebounceSeconds = Number.isFinite(n) && n >= 0 ? n : 0.6;
    });
    row2.append(dBtn, dInput);

    const eBtn = document.createElement('label');
    eBtn.textContent = '过期(s)';
    eBtn.title = '防抖期满后若因对话占用一直推不出去，超过「防抖+过期」仍未推送则静默丢弃该值';
    const eInput = document.createElement('input');
    eInput.type = 'number';
    eInput.min = 0;
    eInput.step = 0.1;
    eInput.className = 's-base-db';
    if (t.ExpireSeconds == null) t.ExpireSeconds = 0.6;
    eInput.value = t.ExpireSeconds;
    eInput.addEventListener('input', () => {
      const n = parseFloat(eInput.value);
      t.ExpireSeconds = Number.isFinite(n) && n >= 0 ? n : 0.6;
    });
    row2.append(eBtn, eInput);

    const fLbl = document.createElement('label');
    fLbl.textContent = '强制推送';
    fLbl.title = '本采样器到可推送时机时，立即用 Chat 打断当前对话并推送（连同其他可推送项）';
    const fCb = document.createElement('input');
    fCb.type = 'checkbox';
    fCb.className = 's-base-cb';
    fCb.checked = t.ForcePush === true;
    fCb.addEventListener('change', () => { t.ForcePush = fCb.checked; });
    row2.append(fLbl, fCb);

    // 第三行：前置采样器 / 采集类型
    const row3 = document.createElement('div');
    row3.className = 'sys-row';
    const pLbl = document.createElement('label');
    pLbl.textContent = '前置采样器';
    pLbl.title = '仅当前置采样器有效时才执行更新和推送（填写采样器名称，留空无前置）';
    const pInput = document.createElement('input');
    pInput.type = 'text';
    pInput.className = 's-base-db';
    pInput.style.width = '100px';
    pInput.placeholder = '采样器名称';
    pInput.value = t.Prerequisite || '';
    pInput.addEventListener('input', () => {
      t.Prerequisite = pInput.value || undefined;
      validatePrerequisite(pInput, t, g);
    });
    validatePrerequisite(pInput, t, g);

    const modeLbl = document.createElement('label');
    modeLbl.textContent = '采集类型';
    const modeSel = document.createElement('select');
    modeSel.className = 't-mode';
    const currentType = t.Sampler || firstType;
    // 当前类型不在已注册列表（缺失/未安装）时，补一个选中但禁用的占位项以明确提示
    const hasType = COLLECTOR_TYPES.some((ct) => ct.type === currentType);
    if (!hasType && t.Sampler) {
      const missing = document.createElement('option');
      missing.value = t.Sampler;
      missing.textContent = `${t.Sampler}（无效）`;
      missing.selected = true;
      missing.disabled = true;
      modeSel.appendChild(missing);
    }
    COLLECTOR_TYPES.forEach((ct) => {
      const o = document.createElement('option');
      o.value = ct.type;
      o.textContent = ct.name;
      o.selected = currentType === ct.type;
      modeSel.appendChild(o);
    });
    modeSel.addEventListener('change', () => {
      t.Sampler = modeSel.value;
      renderTargetList(g);
    });
    row3.append(pLbl, pInput, modeLbl, modeSel);

    top.append(handle, nameLbl, nameInput, vLbl, vCb, copy, del, row2, row3);
    card.appendChild(top);

    // 分割线：通用参数 vs 采样器专属配置
    const divider = document.createElement('hr');
    divider.className = 'sys-divider';
    card.appendChild(divider);

    // 采样器专属配置 UI：由采样器注册的 HTML 片段渲染（前端无特定采样器分支）
    const spec = document.createElement('div');
    spec.className = 't-specific';
    card.appendChild(spec);
    renderTypeFields(spec, t, cfg, i, g);

    list.appendChild(card);
  });
}

function typeEntry(type) {
  return COLLECTOR_TYPES.find((c) => c.type === type) || null;
}

// 渲染采样器自注册的配置 UI 片段；字段通过 data-* 声明，前端用通用约定绑定
function renderTypeFields(spec, t, cfg, i, g) {
  const entry = typeEntry(t.Sampler);
  if (!entry) {
    // 采样器类型缺失（插件未加载或类型已移除）：明确提示，而非静默丢失专属字段
    const warn = document.createElement('div');
    warn.className = 't-missing-type';
    const name = document.createElement('span');
    name.className = 't-missing-name';
    name.textContent = t.Sampler || '(空)';
    warn.append(
      document.createTextNode('采集类型「'),
      name,
      document.createTextNode('」无效或未安装，其专属配置已丢失。请重新选择采集类型，或还原对应插件。')
    );
    spec.appendChild(warn);
    return;
  }
  if (!entry.ui) return;

  const frag = document.createElement('div');
  frag.innerHTML = entry.ui;

  frag.querySelectorAll('[data-regex]').forEach((el) => {
    const key = el.dataset.regex;
    bindScalar(el, cfg, key);
    const row = el.closest('.t-specific-row') || spec;
    const sel = document.createElement('select');
    sel.className = 't-filter';
    const ph = document.createElement('option'); ph.value = '__ph__'; ph.textContent = '选择预设…'; ph.disabled = true; ph.selected = true;
    const dg = document.createElement('option'); dg.value = '\\d+'; dg.textContent = '数字 \\d+';
    const cl = document.createElement('option'); cl.value = '__clear__'; cl.textContent = '不过滤';
    sel.append(ph, dg, cl);
    sel.addEventListener('change', () => {
      if (sel.value === '\\d+') { el.value = '\\d+'; cfg[key] = '\\d+'; }
      else if (sel.value === '__clear__') { el.value = ''; cfg[key] = ''; }
      sel.selectedIndex = 0;
    });
    row.appendChild(sel);
  });

  frag.querySelectorAll('[data-cfg]').forEach((el) => bindScalar(el, cfg, el.dataset.cfg));
  frag.querySelectorAll('[data-region-cfg]').forEach((el) => {
    // 绑定到 region 子字段（如 Radius/StartAngle/SweepAngle），region 字段名取该采样器首个 data-region
    const regionKey = (el.dataset.regionKey) || 'Region';
    const subKey = el.dataset.regionCfg;
    if (!cfg[regionKey]) cfg[regionKey] = {};
    bindScalar(el, cfg[regionKey], subKey);
  });
  frag.querySelectorAll('[data-shape]').forEach((el) => bindShape(el, cfg, el.dataset.shape, g));
  frag.querySelectorAll('[data-region]').forEach((el) => {
    // 有 data-shape 的采样器（颜色类）以形状选择为准；否则按 data-point/data-triangle 属性推断
    const hasShape = el.closest('.t-specific') && el.closest('.t-specific').querySelector('[data-shape]');
    const kind = hasShape
      ? shapeCurrent(cfg, el.dataset.region)
      : (el.hasAttribute('data-point') ? 'point' : (el.hasAttribute('data-triangle') ? 'triangle' : 'rect'));
    bindRegion(el, cfg, el.dataset.region, kind, i);
  });
  frag.querySelectorAll('[data-colors]').forEach((el) => bindColors(el, cfg, el.dataset.colors));
  frag.querySelectorAll('[data-colorhex]').forEach((el) => bindColorHex(el, cfg, el.dataset.colorhex));

  while (frag.firstChild) spec.appendChild(frag.firstChild);
}

// 通用标量绑定：input/select/textarea/checkbox → cfg[key]
function bindScalar(el, cfg, key) {
  if (!key) return;
  const isCheck = el.type === 'checkbox';
  const isNumber = el.type === 'number';
  // 缺省时用元素自带的 value（UI 片段里的默认值）填充并写回配置，避免新建时字段为空
  if (!isCheck && cfg[key] == null && el.value !== '') {
    cfg[key] = isNumber ? (parseFloat(el.value) || 0) : el.value;
  }
  const render = () => { if (isCheck) el.checked = !!cfg[key]; else el.value = cfg[key] == null ? '' : cfg[key]; };
  render();
  const eventName = isCheck ? 'change' : 'input';
  el.addEventListener(eventName, () => {
    cfg[key] = isCheck ? el.checked : (isNumber ? (parseFloat(el.value) || 0) : el.value);
  });
}

// 形状选择：绑定 Shape 配置（point/line/triangle/quad），并让覆盖层按形状提供端点
const SHAPES = [
  { v: 'rect', name: '矩形' },
  { v: 'point', name: '点' },
  { v: 'triangle', name: '三角面' },
  { v: 'sector', name: '扇面' }
];

function bindShape(el, cfg, key, g) {
  if (!key) return;
  if (el.tagName === 'SELECT') {
    el.innerHTML = '';
    SHAPES.forEach((s) => {
      const o = document.createElement('option');
      o.value = s.v;
      o.textContent = s.name;
      o.selected = shapeCurrent(cfg, key) === s.v;
      el.appendChild(o);
    });
    el.addEventListener('change', () => {
      shapeSet(cfg, key, el.value);
      // 形状切换后重渲染，让 region 补齐对应数据（三角→顶点/扇面→参数等）
      if (g) renderTargetList(g);
    });
  }
}

function shapeCurrent(cfg, key) {
  const v = cfg[key];
  if (v && typeof v === 'object') {
    if (v.IsSector) return 'sector';
    if (v.IsTriangle) return 'triangle';
    if (v.IsPoint) return 'point';
    return 'rect';
  }
  return 'rect';
}

function shapeSet(cfg, key, value) {
  const cur = cfg[key];
  if (cur && typeof cur === 'object') {
    cur.IsTriangle = value === 'triangle';
    cur.IsSector = value === 'sector';
    cur.IsPoint = value === 'point';
  } else {
    // Region 尚不存在时，按所选形状创建带正确默认值的区域对象
    if (value === 'point') cfg[key] = defaultPointRegion();
    else if (value === 'triangle') cfg[key] = defaultTriangleRegion();
    else if (value === 'sector') cfg[key] = defaultSectorRegion();
    else cfg[key] = defaultCollectRegion();
  }
}

// 区域字段：矩形(X/Y/W/H) / 点(X/Y) / 三角(IsTriangle+Triangle) / 扇面(IsSector+圆心+半径) + 拖拽编辑按钮
function bindRegion(el, cfg, key, kind, i) {
  const row = el.closest('.t-specific-row') || el.parentElement;
  // region 缺失或缺少矩形/点坐标（X/Width 为空）时，用形状默认值初始化。
  // 注意 data-region-cfg 可能已写入 Radius/StartAngle 等扇面参数，不能据此误判，只看 X/Width。
  const r0 = cfg[key];
  if (!r0 || typeof r0 !== 'object' || (r0.X == null && r0.Width == null)) {
    cfg[key] = (kind === 'point') ? defaultPointRegion() : (kind === 'triangle' ? defaultTriangleRegion() : defaultCollectRegion());
  }
  // 若 region 标记为点/三角/扇面，按对应形状显示
  const r = cfg[key];
  const effKind = (r && r.IsTriangle) ? 'triangle' : ((r && r.IsSector) ? 'sector' : ((r && r.IsPoint) ? 'point' : kind));
  if (effKind === 'triangle' && (!r.Triangle || r.Triangle.length === 0)) {
    // 切到三角但尚无顶点：用当前矩形对角生成三角，保留位置
    if (r && typeof r.X === 'number' && r.Width > 0) {
      r.Triangle = [
        { X: r.X, Y: r.Y },
        { X: r.X + r.Width, Y: r.Y },
        { X: r.X, Y: r.Y + r.Height }
      ];
    } else {
      cfg[key] = defaultTriangleRegion();
    }
  } else if (effKind === 'sector') {
    // 切到扇面：确保有圆心与半径/角度参数
    if (!cfg[key] || cfg[key].Radius == null) cfg[key] = defaultSectorRegion();
    else if (!r.IsSector) { r.IsSector = true; if (r.Radius == null) r.Radius = 100; }
  } else if (effKind === 'point') {
    // 切到点：确保有坐标
    if (typeof r.X !== 'number' || typeof r.Y !== 'number') cfg[key] = defaultPointRegion();
  }
  el.textContent = fmtRegion(cfg[key], effKind);
  // 扇面参数行显隐：作用域限定在本采样器的 UI 容器（片段未挂载时用 row 的父容器）
  const spec = row.closest('.t-specific') || row.parentElement || document;
  spec.querySelectorAll('.sector-cfg').forEach((sc) => { sc.style.display = effKind === 'sector' ? '' : 'none'; });
  const edit = document.createElement('button');
  edit.className = 'small primary';
  edit.textContent = '拖拽编辑';
  edit.addEventListener('click', () => api.showOverlay(buildOverlayItems(`target:${i}:${key}`)));
  row.appendChild(edit);
  // 重置位置：区域被拖出屏幕时，恢复到默认位置（屏幕居中）
  const reset = document.createElement('button');
  reset.className = 'small';
  reset.textContent = '重置位置';
  reset.title = '区域被拖出屏幕看不见时，恢复到屏幕居中';
  reset.addEventListener('click', () => {
    const isPoint = effKind === 'point';
    const isTri = effKind === 'triangle';
    const isSector = effKind === 'sector';
    cfg[key] = isPoint ? defaultPointRegion() : (isTri ? defaultTriangleRegion() : (isSector ? defaultSectorRegion() : defaultCollectRegion()));
    el.textContent = fmtRegion(cfg[key], effKind);
    renderGameEditor();
  });
  row.appendChild(reset);
}

// 默认点：屏幕中心点
function defaultPointRegion() {
  return {
    X: Math.round(window.innerWidth / 2),
    Y: Math.round(window.innerHeight / 2) + 80,
    IsPoint: true
  };
}

// 默认矩形区域：屏幕居中（window 不可用时回退到固定值，避免角坐标 undefined）
function defaultCollectRegion() {
  const w = 120, h = 32;
  const vw = window.innerWidth, vh = window.innerHeight;
  const cx = (Number.isFinite(vw) ? vw : 1920) - w;
  const cy = (Number.isFinite(vh) ? vh : 1080) - h;
  return {
    X: Math.round(cx / 2),
    Y: Math.round(cy / 2) + 80,
    Width: w, Height: h
  };
}

// 默认三角区域：屏幕居中等边三角
function defaultTriangleRegion() {
  const cx = Math.round(window.innerWidth / 2);
  const cy = Math.round(window.innerHeight / 2) + 80;
  return {
    IsTriangle: true,
    Triangle: [
      { X: cx, Y: cy - 60 },
      { X: cx - 70, Y: cy + 60 },
      { X: cx + 70, Y: cy + 60 }
    ]
  };
}

// 默认扇面区域：屏幕居中圆心 + 半径/角度
function defaultSectorRegion() {
  return {
    IsSector: true,
    X: Math.round(window.innerWidth / 2),
    Y: Math.round(window.innerHeight / 2) + 80,
    Radius: 100, StartAngle: 0, SweepAngle: 90
  };
}

// 颜色字段：颜色代称列表编辑器
function bindColors(el, cfg, key) {
  if (!cfg[key]) cfg[key] = [];
  el.classList.add('color-list');
  // 行容器与「添加颜色」按钮分离，按钮常驻不随行重建消失
  const rows = document.createElement('div');
  rows.className = 'color-rows';
  el.appendChild(rows);
  const add = document.createElement('button');
  add.className = 'small';
  add.textContent = '+ 添加颜色';
  add.addEventListener('click', () => {
    cfg[key].push({ Name: `颜色${cfg[key].length + 1}`, Hex: '#FF0000', Tolerance: 3 });
    renderColorRows(rows, cfg, key);
  });
  el.appendChild(add);
  renderColorRows(rows, cfg, key);
}

// 通用颜色字段：调色板 + 屏幕取色 + 十六进制文本双向绑定
function bindColorHex(el, cfg, key) {
  const row = el.closest('.t-specific-row') || el.parentElement;
  const box = document.createElement('div');
  box.className = 't-specific-row';
  const picker = document.createElement('input');
  picker.type = 'color';
  const pickBtn = document.createElement('button');
  pickBtn.className = 'small primary';
  pickBtn.textContent = '屏幕取色';
  pickBtn.title = '点击后在屏幕上取色（编辑窗口会自动隐藏）';
  const hexInput = document.createElement('input');
  hexInput.className = 't-filter-input';
  hexInput.type = 'text';
  hexInput.placeholder = '#RRGGBB';
  const render = () => {
    const v = /^#[0-9a-fA-F]{6}$/.test(cfg[key] || '') ? cfg[key] : '#ff0000';
    picker.value = v;
    hexInput.value = v;
  };
  render();
  picker.addEventListener('input', () => {
    const v = picker.value.toUpperCase();
    cfg[key] = v;
    hexInput.value = v;
  });
  hexInput.addEventListener('input', () => {
    let v = hexInput.value.trim();
    if (!v) { cfg[key] = ''; return; }
    if (v[0] !== '#') v = '#' + v;
    cfg[key] = v;
    if (/^#[0-9a-fA-F]{6}$/.test(v)) picker.value = v;
  });
  pickBtn.addEventListener('click', () => {
    pickTarget = { cfg, key, render };
    api.pickColor();
  });
  box.append(picker, pickBtn, hexInput);
  el.replaceWith(box);
}

function renderColorRows(rows, cfg, key) {
  [...rows.children].forEach((child) => child.remove());
  cfg[key].forEach((c, ci) => {
    const row = document.createElement('div');
    row.className = 'color-item';
    // 屏幕取色：点击在屏幕上识别颜色
    const swatch = document.createElement('span');
    swatch.className = 'c-swatch';
    const renderSwatch = () => {
      swatch.style.background = /^#[0-9a-fA-F]{6}$/.test(c.Hex || '') ? c.Hex : '#ff0000';
    };
    const pickBtn = document.createElement('button');
    pickBtn.className = 'small primary';
    pickBtn.textContent = '取色';
    pickBtn.title = '在屏幕上取色';
    pickBtn.addEventListener('click', () => {
      pickTarget = { cfg: c, key: 'Hex', render: () => { renderSwatch(); hexInput.value = c.Hex || ''; } };
      api.pickColor();
    });
    const hexInput = document.createElement('input');
    hexInput.className = 'c-hex';
    hexInput.type = 'text';
    hexInput.placeholder = '#RRGGBB';
    hexInput.value = /^#[0-9a-fA-F]{6}$/.test(c.Hex || '') ? c.Hex : '#ff0000';
    hexInput.addEventListener('input', () => {
      let v = hexInput.value.trim();
      if (!v) { c.Hex = ''; return; }
      if (v[0] !== '#') v = '#' + v;
      c.Hex = v;
      if (/^#[0-9a-fA-F]{6}$/.test(v)) renderSwatch();
    });
    renderSwatch();
    const alias = document.createElement('input');
    alias.className = 'c-alias';
    alias.type = 'text';
    alias.placeholder = '代称，如：红';
    alias.value = c.Name || '';
    alias.addEventListener('input', () => { c.Name = alias.value; });
    const tolLbl = document.createElement('span');
    tolLbl.className = 'c-tol-label';
    tolLbl.textContent = '容差';
    tolLbl.title = '颜色匹配允许误差（RGB 距离上限）';
    const tol = document.createElement('input');
    tol.className = 'c-tol';
    tol.type = 'number';
    tol.min = 0;
    tol.max = 255;
    tol.title = '颜色匹配允许误差';
    tol.value = c.Tolerance == null ? 3 : c.Tolerance;
    tol.addEventListener('input', () => {
      const n = parseInt(tol.value, 10);
      c.Tolerance = Number.isFinite(n) ? n : 3;
    });
    const del = document.createElement('button');
    del.className = 'small danger';
    del.textContent = '✕';
    del.addEventListener('click', () => { cfg[key].splice(ci, 1); renderColorRows(rows, cfg, key); });
    row.append(swatch, pickBtn, hexInput, alias, tolLbl, tol, del);
    rows.appendChild(row);
  });
}

function addTarget() {
  const g = currentGame();
  if (!g) return;
  const defType = COLLECTOR_TYPES.length > 0 ? COLLECTOR_TYPES[0].type : 'text';
  const idx = (g.Collectors?.length || 0) + 1;
  g.Collectors.push({ Name: `目标${idx}`, Sampler: defType, Config: {} });
  renderTargetList(g);
}

// 按比例转换当前游戏的所有采集区域到目标分辨率，并更新基准分辨率。
// 目标分辨率优先取用户自定义输入，否则为当前物理分辨率。
// 基准未记录(0)时仅记录目标分辨率为基准（scale=1，不缩放区域）。
function convertResolution() {
  const g = currentGame();
  if (!g) return;
  const tgt = targetRes();
  if (!(tgt.width > 0 && tgt.height > 0)) return;
  const baseW = g.BaseScreenWidth, baseH = g.BaseScreenHeight;

  if (baseW > 0 && baseH > 0 && (baseW !== tgt.width || baseH !== tgt.height)) {
    const sx = tgt.width / baseW;
    const sy = tgt.height / baseH;

    const scalePoint = (p) => {
      if (!p) return p;
      p.X = Math.round(p.X * sx);
      p.Y = Math.round(p.Y * sy);
      return p;
    };
    const scaleRegion = (r) => {
      if (!r || typeof r !== 'object') return;
      if (r.X != null) r.X = Math.round(r.X * sx);
      if (r.Y != null) r.Y = Math.round(r.Y * sy);
      if (r.Width != null) r.Width = Math.round(r.Width * sx);
      if (r.Height != null) r.Height = Math.round(r.Height * sy);
      if (r.Radius != null) r.Radius = Math.round(r.Radius * Math.min(sx, sy));
      if (Array.isArray(r.Triangle)) r.Triangle.forEach(scalePoint);
    };

    (g.Collectors || []).forEach((t) => {
      const cfg = t.Config || {};
      Object.keys(cfg).forEach((key) => {
        const v = cfg[key];
        if (v && typeof v === 'object' && !Array.isArray(v)) {
          // 区域对象（含 Region / 各类形状），跳过颜色代称数组、字符串等
          if ('X' in v || 'Width' in v || 'Radius' in v || 'Triangle' in v || v.IsSector || v.IsPoint || v.IsTriangle !== undefined) {
            scaleRegion(v);
          }
        }
      });
    });
  }

  // 更新基准分辨率并落盘
  g.BaseScreenWidth = tgt.width;
  g.BaseScreenHeight = tgt.height;
  saveConfigNow();
  renderGameEditor();
}

// 手动保存配置到磁盘（编辑器右上角按钮）
function saveConfigNow() {
  try {
    api.saveConfigSync(config);
    flashSaved();
  } catch (e) {
    console.error('保存配置失败', e);
    alert('保存配置失败：' + (e && e.message ? e.message : e));
  }
}

// 保存成功时按钮短暂高亮提示
function flashSaved() {
  const btn = document.getElementById('btn-save-config');
  if (!btn) return;
  const orig = btn.textContent;
  btn.textContent = '已保存 ✓';
  btn.classList.add('saved');
  setTimeout(() => { btn.textContent = orig; btn.classList.remove('saved'); }, 1200);
}

// ---------- 覆盖层交互 ----------
// 采样器的区域字段按注册的 UI 片段（data-region）推导，供覆盖层绘制
const __regionFieldsCache = {};
function regionFieldsOf(type) {
  if (__regionFieldsCache[type]) return __regionFieldsCache[type];
  const entry = typeEntry(type);
  const fields = [];
  if (entry && entry.ui) {
    const d = document.createElement('div');
    d.innerHTML = entry.ui;
    d.querySelectorAll('[data-region]').forEach((el) => fields.push({
      key: el.dataset.region,
      kind: el.hasAttribute('data-point') ? 'point' : (el.hasAttribute('data-triangle') ? 'triangle' : 'rect')
    }));
  }
  __regionFieldsCache[type] = fields;
  return fields;
}

function buildOverlayItems(selectedId) {
  const g = currentGame();
  if (!g) return [];
  const items = [];
  (g.Collectors || []).forEach((t, i) => {
    if (!t.Config) return;
    regionFieldsOf(t.Sampler).forEach((f) => {
      const cfg = t.Config;
      const region = cfg[f.key] || {};
      const kind = region.IsTriangle ? 'triangle' : (region.IsSector ? 'sector' : (region.IsPoint ? 'point' : f.kind));
      items.push({
        id: `target:${i}:${f.key}`,
        label: (t.Name || `目标${i + 1}`) + '·' + f.key,
        kind,
        color: kind === 'point' ? '#52c41a' : (kind === 'triangle' ? '#fa8c16' : (kind === 'sector' ? '#722ed1' : '#1677ff')),
        region: kind === 'triangle' || kind === 'sector' ? null : { X: region.X || 0, Y: region.Y || 0, Width: region.Width || 0, Height: region.Height || 0 },
        triangle: kind === 'triangle' ? (region.Triangle || []) : null,
        sector: kind === 'sector' ? { X: region.X || 0, Y: region.Y || 0, Radius: region.Radius || 100, StartAngle: region.StartAngle || 0, SweepAngle: region.SweepAngle || 90 } : null
      });
    });
  });
  items.forEach((it) => { it.selected = it.id === selectedId; });
  return items;
}

function applyRegion(id, region) {
  const g = currentGame();
  if (!g) return;
  const m = /^target:(\d+):(.+)$/.exec(id || '');
  if (!m) return;
  const i = parseInt(m[1], 10);
  const key = m[2];
  const t = g.Collectors[i];
  if (t && t.Config) {
    if (region && region.IsPoint !== undefined) {
      // 点数据
      t.Config[key].IsPoint = true;
      t.Config[key].IsTriangle = false;
      t.Config[key].IsSector = false;
      t.Config[key].X = region.X;
      t.Config[key].Y = region.Y;
    } else if (region && region.IsTriangle !== undefined) {
      // 三角数据
      t.Config[key].IsTriangle = true;
      t.Config[key].IsSector = false;
      t.Config[key].Triangle = region.Triangle || [];
    } else if (region && region.IsSector !== undefined) {
      // 扇面数据
      t.Config[key] = region;
      t.Config[key].IsSector = true;
      t.Config[key].IsTriangle = false;
    } else {
      // 矩形数据
      t.Config[key] = region;
      t.Config[key].IsTriangle = false;
      t.Config[key].IsSector = false;
    }
  }
  renderGameEditor();
}

// ---------- 保存（仅在关闭窗口时同步落盘，见 beforeunload） ----------
