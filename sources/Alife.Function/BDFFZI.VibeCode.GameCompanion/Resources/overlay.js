// 全屏悬浮区域编辑器 - 覆盖层逻辑
// 组件类型：point(点) / rect(矩形) / triangle(三角面)
// 覆盖层渲染不参与 DPR 缩放换算：窗口本身是 1:1 物理像素，直接用坐标。
const api = window.companion;

const canvas = document.getElementById('canvas');
const ctx = canvas.getContext('2d');
const itemSelect = document.getElementById('item-select');
const coordEl = document.getElementById('coord');

let items = [];
let selectedId = null;
let scaleFactor = 1;
let pickMode = false;
let pickCursor = null; // 取色十字准星位置 {x,y}（物理像素）

// 只读查看模式（?view=1）：仅显示区域范围，隐藏 HUD、不响应鼠标、不阻塞穿透
const viewMode = new URLSearchParams(location.search).get('view') === '1';

// 拖拽状态：{ id, kind, mode:'move'|'vertex'|'resize', index, handle, startX, startY, startData }
let drag = null;

const HANDLE = 7; // 命中范围

async function init() {
  // 截图/采样为物理像素(如1920x1080)，覆盖层也用物理像素坐标系；
  // 鼠标 clientX 为 CSS 逻辑像素，需乘 DPR 转物理，保证与配置坐标一致。
  scaleFactor = window.devicePixelRatio || 1;
  const physW = window.screen.width * scaleFactor;
  const physH = window.screen.height * scaleFactor;
  canvas.width = physW;
  canvas.height = physH;
  canvas.style.width = (physW / scaleFactor) + 'px';
  canvas.style.height = (physH / scaleFactor) + 'px';

  api.debugSend(JSON.stringify({ evt: 'init', scaleFactor, screenW: window.screen.width, screenH: window.screen.height, physW, physH, viewMode }));

  if (viewMode) {
    // 只读查看：隐藏 HUD，画布不拦截鼠标，直接绘制所有区域
    const hud = document.getElementById('hud');
    if (hud) hud.style.display = 'none';
    canvas.style.pointerEvents = 'none';
  }

  api.onPickMode((on) => {
    if (viewMode) return;
    pickMode = on;
    pickCursor = null;
    const hint = document.getElementById('coord');
    if (hint) hint.textContent = on ? '点击屏幕任意处取色 (Esc 取消)' : '未选中区域';
    if (on) { renderItemSelect(); draw(); }
  });

  api.onOverlayItems((list) => {
    if (pickMode) return;
    items = (list || []).map(normalizeItem);
    if (viewMode) {
      selectedId = null;
      draw();
      return;
    }
    if (items.length > 0) {
      const sel = items.find((i) => i.selected) || items.find(isOnScreen) || items[0];
      selectedId = sel.id;
    }
    renderItemSelect();
    updateCoord();
    draw();
  });

  if (viewMode) {
    draw();
    return;
  }

  document.getElementById('btn-finish').addEventListener('click', finish);
  itemSelect.addEventListener('change', () => {
    if (pickMode) return;
    selectedId = itemSelect.value;
    updateCoord();
    draw();
  });
  window.addEventListener('keydown', onKeyDown);

  setupHudDrag();

  canvas.addEventListener('mousedown', onMouseDown);
  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mouseup', onMouseUp);
  window.addEventListener('mousemove', onPickMove);
  draw();
}

// 归一化：kind ∈ 'point'|'rect'|'triangle'|'sector'
function normalizeItem(raw) {
  if (!raw) return raw;
  const kind = raw.kind || (raw.shape === 'triangle' ? 'triangle' : (raw.shape === 'sector' ? 'sector' : (raw.shape === 'point' ? 'point' : 'rect')));
  let region = raw.region || raw.triangle || null;
  if (kind === 'triangle') {
    // 三角：3 个顶点
    let tri = raw.triangle || raw.region;
    if (tri && Array.isArray(tri)) return { ...raw, kind, triangle: tri.map(normPt) };
    // 旧 {X,Y,W,H} 转三角
    const r = raw.region || { X: 0, Y: 0, Width: 0, Height: 0 };
    return { ...raw, kind, triangle: [
      { X: r.X, Y: r.Y },
      { X: r.X + (r.Width || 0), Y: r.Y },
      { X: r.X, Y: r.Y + (r.Height || 0) }
    ] };
  }
  if (kind === 'sector') {
    const s = raw.sector || raw.region || {};
    return { ...raw, kind, sector: { X: s.X || 0, Y: s.Y || 0, Radius: s.Radius || 100, StartAngle: s.StartAngle || 0, SweepAngle: s.SweepAngle || 90 } };
  }
  return { ...raw, kind, region: { X: raw.region?.X || 0, Y: raw.region?.Y || 0, Width: raw.region?.Width || 0, Height: raw.region?.Height || 0 } };
}

function normPt(p) { return { X: p.X || 0, Y: p.Y || 0 }; }

// ---- HUD 拖拽 ----
let hudDrag = null;
function setupHudDrag() {
  const hud = document.getElementById('hud');
  const title = document.querySelector('.hud-title');
  title.addEventListener('mousedown', (e) => {
    e.stopPropagation();
    e.preventDefault();
    const rect = hud.getBoundingClientRect();
    hudDrag = { startX: e.clientX, startY: e.clientY, origLeft: rect.left, origTop: rect.top };
    window.addEventListener('mousemove', onHudDragMove);
  });
  window.addEventListener('mouseup', () => {
    if (!hudDrag) return;
    hudDrag = null;
    window.removeEventListener('mousemove', onHudDragMove);
  });
}
function onHudDragMove(e) {
  if (!hudDrag) return;
  const hud = document.getElementById('hud');
  const dx = e.clientX - hudDrag.startX;
  const dy = e.clientY - hudDrag.startY;
  const maxX = Math.max(0, window.screen.width - hud.offsetWidth);
  const maxY = Math.max(0, window.screen.height - hud.offsetHeight);
  const left = Math.min(Math.max(0, hudDrag.origLeft + dx), maxX);
  const top = Math.min(Math.max(0, hudDrag.origTop + dy), maxY);
  hud.style.left = left + 'px';
  hud.style.top = top + 'px';
}

function isOnScreen(it) {
  let pts;
  if (it.kind === 'triangle') pts = it.triangle;
  else if (it.kind === 'sector') pts = [{ X: it.sector.X - it.sector.Radius, Y: it.sector.Y - it.sector.Radius }, { X: it.sector.X + it.sector.Radius, Y: it.sector.Y + it.sector.Radius }];
  else pts = [it.region, { X: it.region.X + it.region.Width, Y: it.region.Y + it.region.Height }];
  if (!pts || pts.length === 0) return false;
  const cssW = canvas.width, cssH = canvas.height;
  let anyVisible = false;
  pts.forEach((p) => {
    if (p.X >= 0 && p.Y >= 0 && p.X <= cssW + 2 && p.Y <= cssH + 2) anyVisible = true;
  });
  return anyVisible;
}

function renderItemSelect() {
  itemSelect.innerHTML = '';
  items.forEach((it) => {
    const opt = document.createElement('option');
    opt.value = it.id;
    opt.textContent = it.label || it.id;
    opt.selected = it.id === selectedId;
    itemSelect.appendChild(opt);
  });
  if (items.length === 0) {
    const opt = document.createElement('option');
    opt.textContent = '（暂无区域）';
    itemSelect.appendChild(opt);
  }
}

// ---- 绘制 ----
function draw() {
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, canvas.width, canvas.height);

  // 取色模式：只画十字准星 + 放大镜
  if (pickMode) {
    if (pickCursor) {
      const { x, y } = pickCursor;
      ctx.strokeStyle = 'rgba(255,255,255,0.9)';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(x - 14, y); ctx.lineTo(x + 14, y);
      ctx.moveTo(x, y - 14); ctx.lineTo(x, y + 14);
      ctx.stroke();
      ctx.strokeStyle = 'rgba(0,0,0,0.6)';
      ctx.beginPath();
      ctx.arc(x, y, 14, 0, Math.PI * 2);
      ctx.stroke();
      const hint = document.getElementById('coord');
      if (hint) hint.textContent = `取色 (${Math.round(x)}, ${Math.round(y)}) · Esc 取消`;
    }
    return;
  }

  if (items.length === 0) {
    ctx.fillStyle = 'rgba(255,255,255,0.85)';
    ctx.font = '16px "Microsoft YaHei"';
    ctx.textAlign = 'center';
    ctx.fillText('暂无区域可编辑，请先在主窗口添加验证/采集目标', canvas.width / 2, canvas.height / 2);
    return;
  }
  items.forEach((it) => {
    const sel = it.id === selectedId;
    // 有选中目标时，未选中的以虚线只读呈现；无选中则全部虚线只读
    if (selectedId && !sel) ctx.setLineDash([6, 4]);
    if (it.kind === 'point') drawPoint(it, sel);
    else if (it.kind === 'triangle') drawTriangle(it, sel);
    else if (it.kind === 'sector') drawSector(it, sel);
    else drawRect(it, sel);
    ctx.setLineDash([]);
  });
}

function drawSector(it, sel) {
  const s = it.sector;
  const cx = s.X, cy = s.Y, r = Math.max(1, s.Radius);
  const a0 = s.StartAngle, sweep = s.SweepAngle;
  const rad0 = a0 * Math.PI / 180, rad1 = (a0 + sweep) * Math.PI / 180;
  // 填充扇面
  ctx.fillStyle = it.color;
  ctx.globalAlpha = sel ? 0.30 : 0.24;
  ctx.beginPath();
  ctx.moveTo(cx, cy);
  ctx.arc(cx, cy, r, rad0, rad1);
  ctx.closePath();
  ctx.fill();
  ctx.globalAlpha = 1;
  // 描边（弧 + 两条半径边）
  ctx.strokeStyle = it.color;
  ctx.lineWidth = sel ? 3 : 2;
  ctx.beginPath();
  ctx.moveTo(cx, cy);
  ctx.lineTo(cx + r * Math.cos(rad0), cy + r * Math.sin(rad0));
  ctx.arc(cx, cy, r, rad0, rad1);
  ctx.lineTo(cx, cy);
  ctx.stroke();
  // 圆心 + 半径端点手柄 + 角度端点
  [[cx, cy], [cx + r * Math.cos(rad0), cy + r * Math.sin(rad0)], [cx + r * Math.cos(rad1), cy + r * Math.sin(rad1)]].forEach(([hx, hy]) => {
    ctx.fillStyle = '#fff';
    ctx.strokeStyle = 'rgba(0,0,0,0.6)';
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(hx, hy, sel ? 6 : 5, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  });
  // 采样器名称标签放在扇区中间（角度中分线、半径一半处）
  const midRad = (a0 + sweep / 2) * Math.PI / 180;
  drawLabel(it, cx + (r / 2) * Math.cos(midRad), cy + (r / 2) * Math.sin(midRad));
}

function drawPoint(it, sel) {
  const p = it.region;
  const x = p.X, y = p.Y;
  const size = sel ? 12 : 10;
  ctx.strokeStyle = it.color;
  ctx.lineWidth = sel ? 3 : 2;
  ctx.beginPath();
  ctx.moveTo(x - size, y); ctx.lineTo(x + size, y);
  ctx.moveTo(x, y - size); ctx.lineTo(x, y + size);
  ctx.stroke();
  ctx.beginPath(); ctx.arc(x, y, sel ? 9 : 7, 0, Math.PI * 2); ctx.stroke();
  drawLabel(it, x + size + 4, y - size - 4);
}

function drawRect(it, sel) {
  const r = it.region;
  const rx = r.X, ry = r.Y, rw = r.Width, rh = r.Height;
  ctx.fillStyle = it.color;
  ctx.globalAlpha = sel ? 0.30 : 0.24;
  ctx.fillRect(rx, ry, rw, rh);
  ctx.globalAlpha = 1;
  ctx.strokeStyle = it.color;
  ctx.lineWidth = sel ? 3 : 2;
  ctx.strokeRect(rx, ry, rw, rh);
  ctx.strokeStyle = 'rgba(255,255,255,0.9)';
  ctx.lineWidth = 1;
  ctx.strokeRect(rx - 1.5, ry - 1.5, rw + 3, rh + 3);
  if (sel) {
    ctx.fillStyle = '#fff';
    ctx.strokeStyle = it.color;
    ctx.lineWidth = 2;
    [[rx, ry], [rx + rw, ry], [rx, ry + rh], [rx + rw, ry + rh]].forEach(([hx, hy]) => {
      ctx.beginPath(); ctx.arc(hx, hy, HANDLE + 1, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
    });
  }
  drawLabel(it, rx, ry - 6);
}

function drawTriangle(it, sel) {
  const pts = it.triangle;
  ctx.fillStyle = it.color;
  ctx.globalAlpha = sel ? 0.30 : 0.24;
  ctx.beginPath();
  ctx.moveTo(pts[0].X, pts[0].Y);
  ctx.lineTo(pts[1].X, pts[1].Y);
  ctx.lineTo(pts[2].X, pts[2].Y);
  ctx.closePath();
  ctx.fill();
  ctx.globalAlpha = 1;
  ctx.strokeStyle = it.color;
  ctx.lineWidth = sel ? 3 : 2;
  ctx.stroke();
  pts.forEach((p) => {
    ctx.fillStyle = '#fff';
    ctx.strokeStyle = 'rgba(0,0,0,0.6)';
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.arc(p.X, p.Y, sel ? 6 : 5, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
  });
  // 采样器名称标签放在三角形质心（三顶点平均）
  drawLabel(it, (pts[0].X + pts[1].X + pts[2].X) / 3, (pts[0].Y + pts[1].Y + pts[2].Y) / 3);
}

function drawLabel(it, x, y) {
  const tag = it.kind === 'point' ? ' (点)' : it.kind === 'triangle' ? ' (三角)' : it.kind === 'sector' ? ' (扇面)' : '';
  const text = `${it.label}${tag}`;
  ctx.font = '12px "Microsoft YaHei"';
  ctx.textAlign = 'left';
  ctx.textBaseline = 'alphabetic';
  const w = ctx.measureText(text).width + 8;
  ctx.fillStyle = 'rgba(0,0,0,0.7)';
  ctx.fillRect(x - 2, y - 14, w, 18);
  ctx.fillStyle = '#fff';
  ctx.fillText(text, x + 2, y);
}

// ---- 交互 ----
function onMouseDown(e) {
  document.body.dataset.editing = '1'; // 标记正在编辑，监控采样时可跳过隐藏覆盖层避免闪烁
  const mx = e.clientX * scaleFactor, my = e.clientY * scaleFactor;
  if (pickMode) { doPickAt(mx, my); e.preventDefault(); return; }
  const hit = hitTest(mx, my);
  if (!hit) return;
  selectedId = hit.id;
  const it = items.find((i) => i.id === hit.id);
  drag = {
    id: hit.id, kind: it.kind, mode: hit.mode, index: hit.index, handle: hit.handle,
    startX: mx, startY: my,
    startRegion: it.region ? { ...it.region } : null,
    startTriangle: it.triangle ? it.triangle.map(normPt) : null,
    startSector: it.sector ? { ...it.sector } : null
  };
  renderItemSelect();
  draw();
  e.preventDefault();
}

function onMouseMove(e) {
  if (!drag) return;
  const it = items.find((i) => i.id === drag.id);
  if (!it) return;
  const mx = e.clientX * scaleFactor, my = e.clientY * scaleFactor;
  const dx = mx - drag.startX;
  const dy = my - drag.startY;

  if (drag.mode === 'vertex') {
    // 三角顶点跟随鼠标（绝对位置，物理像素）
    it.triangle[drag.index] = { X: mx, Y: my };
  } else if (drag.mode === 'sector-move') {
    // 移动扇面：圆心跟随鼠标
    const st = drag.startSector || it.sector;
    it.sector.X = st.X + dx;
    it.sector.Y = st.Y + dy;
  } else if (drag.mode === 'sector-radius') {
    // 拖半径端点：改半径 + 起始角
    const st = drag.startSector || it.sector;
    const rx = mx - st.X, ry = my - st.Y;
    it.sector.Radius = Math.max(1, Math.round(Math.hypot(rx, ry)));
    it.sector.StartAngle = Math.round((Math.atan2(ry, rx) * 180 / Math.PI + 360) % 360);
  } else if (drag.mode === 'sector-angle') {
    // 拖角度端点：改扫过角（从起始角到鼠标角度）
    const st = drag.startSector || it.sector;
    const rx = mx - st.X, ry = my - st.Y;
    const mouseAng = (Math.atan2(ry, rx) * 180 / Math.PI + 360) % 360;
    let sweep = mouseAng - st.StartAngle;
    if (sweep < 0) sweep += 360;
    it.sector.SweepAngle = Math.round(Math.max(1, Math.min(360, sweep)));
    it.sector.Radius = Math.max(1, Math.round(Math.hypot(rx, ry)));
  } else if (drag.mode === 'move') {
    if (it.kind === 'triangle') {
      // 基于拖拽起始值 + 相对位移，避免 += 重复累加导致飞远
      const st = drag.startTriangle || it.triangle;
      it.triangle = st.map((p) => ({ X: p.X + dx, Y: p.Y + dy }));
    } else {
      const st = drag.startRegion || it.region;
      it.region.X = st.X + dx;
      it.region.Y = st.Y + dy;
    }
  } else if (drag.mode === 'resize') {
    // 矩形缩放：基于拖拽起始值 + 相对位移（handle 为 nw/ne/sw/se 双字母）
    const st = drag.startRegion || it.region;
    const r = it.region;
    const h = drag.handle;
    const isN = h.includes('n'), isS = h.includes('s'), isW = h.includes('w'), isE = h.includes('e');
    // X/宽：西边随鼠标移动，东边固定
    if (isW) { r.X = st.X + dx; r.Width = st.Width - dx; }
    else { r.X = st.X; r.Width = st.Width + dx; }
    // Y/高：北边随鼠标移动，南边固定
    if (isN) { r.Y = st.Y + dy; r.Height = st.Height - dy; }
    else { r.Y = st.Y; r.Height = st.Height + dy; }
    if (r.Width < 4) r.Width = 4;
    if (r.Height < 4) r.Height = 4;
  }

  api.reportRegionChange(it.id, dataOf(it));
  updateCoord();
  draw();
}

function onMouseUp() {
  document.body.dataset.editing = ''; // 编辑结束，恢复监控采样时的覆盖层隐藏
  if (drag) {
    const it = items.find((i) => i.id === drag.id);
    if (it) api.reportRegionChange(it.id, dataOf(it));
    drag = null;
  }
}

function dataOf(it) {
  if (it.kind === 'triangle') return { IsTriangle: true, Triangle: it.triangle };
  if (it.kind === 'sector') return { IsSector: true, X: it.sector.X, Y: it.sector.Y, Radius: it.sector.Radius, StartAngle: it.sector.StartAngle, SweepAngle: it.sector.SweepAngle };
  if (it.kind === 'point') return { IsPoint: true, X: it.region.X, Y: it.region.Y };
  return it.region;
}

function hitTest(mx, my) {
  // 仅让已选中的目标可交互（拖拽/缩放），未选中的虚线只读；无选中时仍可点选任一
  const candidates = selectedId ? items.filter((i) => i.id === selectedId) : items;
  for (let i = candidates.length - 1; i >= 0; i--) {
    const it = candidates[i];
    if (it.kind === 'point') {
      if (Math.hypot(mx - it.region.X, my - it.region.Y) <= 14) return { id: it.id, mode: 'move' };
    } else if (it.kind === 'triangle') {
      // 顶点
      for (let vi = 0; vi < it.triangle.length; vi++) {
        if (Math.hypot(mx - it.triangle[vi].X, my - it.triangle[vi].Y) <= HANDLE + 3) return { id: it.id, mode: 'vertex', index: vi };
      }
      if (pointInTri(mx, my, it.triangle)) return { id: it.id, mode: 'move' };
    } else if (it.kind === 'sector') {
      const s = it.sector;
      const cx = s.X, cy = s.Y, r = Math.max(1, s.Radius);
      const rad0 = s.StartAngle * Math.PI / 180, rad1 = (s.StartAngle + s.SweepAngle) * Math.PI / 180;
      // 圆心
      if (Math.hypot(mx - cx, my - cy) <= HANDLE + 3) return { id: it.id, mode: 'sector-move' };
      // 半径端点（起边）
      const e0x = cx + r * Math.cos(rad0), e0y = cy + r * Math.sin(rad0);
      if (Math.hypot(mx - e0x, my - e0y) <= HANDLE + 3) return { id: it.id, mode: 'sector-radius' };
      // 角度端点（终边）
      const e1x = cx + r * Math.cos(rad1), e1y = cy + r * Math.sin(rad1);
      if (Math.hypot(mx - e1x, my - e1y) <= HANDLE + 3) return { id: it.id, mode: 'sector-angle' };
      // 扇面内部移动
      if (pointInSector(mx, my, s)) return { id: it.id, mode: 'sector-move' };
    } else {
      const r = it.region;
      const corners = { nw: [r.X, r.Y], ne: [r.X + r.Width, r.Y], sw: [r.X, r.Y + r.Height], se: [r.X + r.Width, r.Y + r.Height] };
      for (const [h, [cx, cy]] of Object.entries(corners)) {
        if (Math.hypot(mx - cx, my - cy) <= HANDLE + 3) return { id: it.id, mode: 'resize', handle: h };
      }
      if (mx >= r.X && mx <= r.X + r.Width && my >= r.Y && my <= r.Y + r.Height) return { id: it.id, mode: 'move' };
    }
  }
  return null;
}

function pointInSector(px, py, s) {
  const dx = px - s.X, dy = py - s.Y;
  if (dx * dx + dy * dy > (s.Radius || 1) * (s.Radius || 1)) return false;
  const ang = (Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360;
  const start = ((s.StartAngle || 0) % 360 + 360) % 360;
  const sweep = Math.max(0, Math.min(360, s.SweepAngle || 0));
  if (sweep >= 360) return true;
  const end = start + sweep;
  if (end > 360) return ang >= start || ang < end - 360;
  return ang >= start && ang < end;
}

function pointInTri(px, py, tri) {
  const [a, b, c] = tri;
  const d1 = sign(px, py, a.X, a.Y, b.X, b.Y);
  const d2 = sign(px, py, b.X, b.Y, c.X, c.Y);
  const d3 = sign(px, py, c.X, c.Y, a.X, a.Y);
  const hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
  const hasPos = d1 > 0 || d2 > 0 || d3 > 0;
  return !(hasNeg && hasPos);
}
function sign(px, py, ax, ay, bx, by) { return (px - bx) * (ay - by) - (ax - bx) * (py - by); }

function updateCoord() {
  const it = items.find((i) => i.id === selectedId);
  if (!it) { coordEl.textContent = '未选中区域'; return; }
  if (it.kind === 'point') coordEl.textContent = `点位: (${it.region.X}, ${it.region.Y})`;
  else if (it.kind === 'triangle') coordEl.textContent = `三角: ` + it.triangle.map((p) => `(${p.X},${p.Y})`).join(' ');
  else if (it.kind === 'sector') coordEl.textContent = `扇面: 圆心(${it.sector.X},${it.sector.Y}) 半径${it.sector.Radius} ${it.sector.StartAngle}°~${it.sector.StartAngle + it.sector.SweepAngle}°`;
  else coordEl.textContent = `区域: x=${it.region.X} y=${it.region.Y} w=${it.region.Width} h=${it.region.Height}`;
}

function finish() {
  if (pickMode) { api.pickCancel(); pickMode = false; pickCursor = null; }
  else api.finishOverlay();
}

// 键盘移动：方向键微调选中元素（物理像素），Shift 加速
function onKeyDown(e) {
  if (e.key === 'Escape') { finish(); return; }
  const it = items.find((i) => i.id === selectedId);
  if (!it || pickMode) return;
  const step = e.shiftKey ? 10 : 1;
  const dirs = {
    ArrowLeft: [-step, 0], ArrowRight: [step, 0],
    ArrowUp: [0, -step], ArrowDown: [0, step]
  };
  if (!dirs[e.key]) return;
  e.preventDefault();
  moveSelectedBy(it, dirs[e.key][0], dirs[e.key][1]);
}

// 按增量移动选中元素（矩形/点移动 X/Y；三角整体平移；扇面移动圆心）
function moveSelectedBy(it, dx, dy) {
  if (it.kind === 'triangle') {
    it.triangle = it.triangle.map((p) => ({ X: p.X + dx, Y: p.Y + dy }));
  } else if (it.kind === 'sector') {
    it.sector.X += dx; it.sector.Y += dy;
  } else {
    it.region.X += dx; it.region.Y += dy;
  }
  api.reportRegionChange(it.id, dataOf(it));
  updateCoord();
  draw();
}

// 取色模式：鼠标移动更新十字准星位置（物理像素）
function onPickMove(e) {
  if (!pickMode) return;
  pickCursor = { x: e.clientX * scaleFactor, y: e.clientY * scaleFactor };
  draw();
}

// 取色点击：由 onMouseDown 在 pickMode 时调用
function doPickAt(x, y) {
  api.pickAt(x, y).then((hex) => {
    pickMode = false;
    pickCursor = null;
    api.pickResult(hex);
  });
}

init();