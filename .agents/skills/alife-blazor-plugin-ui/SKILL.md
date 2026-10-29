---
name: alife-blazor-plugin-ui
description: 在 Alife 项目中开发或调试插件前端界面（Blazor Hybrid + AntDesign）时使用：编写或修改 *UI.razor / 模块界面、插件热重载、WebView2 远程调试、Razor 事件不生效、布局错乱、界面数字对不上。正文即完整指南，动手前先读 §1–§3，交付前按 §8 清单核查。
---

# Blazor 插件 UI 开发与调试指南

> 面向：要在 Alife 里写插件界面（Blazor Hybrid + AntDesign）的同学。
> 这份文档只讲**通用的方法与坑**，不绑定具体功能。例子取自 Token 统计面板的实现过程。
> 最后更新：2026-10-29

---

## 0. 一句话总结

> 在这个环境里写 UI，**难点不在 Blazor 语法，而在三件事**：
> ① 事件在这套 Razor 编译下不按官方预期工作；② 你必须能"看到"渲染结果；③ 页面上的数字必须能互相解释。
> 下面按这三条展开。

---

## 1. 环境准备：先让自己能"看见"和"操作"

### 1.1 以调试端口启动客户端

WebView2 默认不开远程调试。启动前设置环境变量：

```powershell
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=9222"
Start-Process -FilePath "dotnet" -ArgumentList @('run','--project','Sources\Alife.Client\Alife.Client.Windows\Alife.Client.Windows.csproj') -WorkingDirectory "C:\Users\13309\Desktop\Alife"
```

验证：

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:9222/json/list" -TimeoutSec 5 | Select-Object type, url
```

**注意**：`Start-Job`/前台调用启动的进程会随 PowerShell 会话结束被杀掉；要用 `Start-Process` 让它独立存活。

### 1.2 MCP（插件热重载）

插件有 MCP 服务（默认 `http://127.0.0.1:18765/mcp`），常用工具：

| 工具 | 用途 |
|---|---|
| `reload_plugin` | 重新加载指定插件（改完代码后） |
| 其它 | 取决于插件自己暴露了什么 |

**连接失败的两种情况**：① 客户端没启动；② 客户端刚启动还没监听（等 30~60 秒）。先 `Get-NetTCPConnection -LocalPort 18765` 确认端口在监听再重试。

---

## 2. 开发循环（照这个顺序走，别跳步）

```
改代码
  ↓
dotnet build <插件 csproj>            ← 必须 0 error
  ↓
MCP: reload_plugin                    ← 有时要调用两次（见 2.1）
  ↓
导航离开再回来（/welcome → /system → 点击插件标签页）
  ↓
验证：截图看 + DOM 断言 + 服务层断言   ← 三者都要
```

### 2.1 热重载偶发加载旧 DLL（必踩坑）

**症状**：代码改了、build 也过了、页面却像没生效（某个改动"消失"了）。

**原因**：热重载有时加载到上一次的程序集。

**识别与处理**：
1. 先在 DOM 里断言"新加的元素/类名/文本"是否存在；
2. 不存在 → 再 `reload_plugin` 一次，然后重新导航；
3. 还不行 → 重启客户端。

真实案例：一次 UI 改动后「思考」这个分类从排行里消失，排查半小时发现服务层输出正常，是插件加载了上一版 DLL；干净重载后立刻恢复正常。**结论：改动"没生效"时，先怀疑重载，不要先怀疑逻辑。**

---

## 3. 这套环境里 Razor 的已知限制（最重要的一节）

### 3.1 原生 `@on*` 事件在插件里不生效

```razor
<!-- ✗ 不生效：编译成字面属性，永远不触发 -->
<div @onclick="() => DoSomething()">点击</div>
```

**必须用组件的回调参数**。可用清单（都在本仓库验证过）：

| 组件 | 用法 |
|---|---|
| `Button` | `OnClick="@(() => ...)"` |
| `Checkbox` | `@bind-Value="field"` |
| `RadioGroup` + `Radio RadioButton` | `Value="@x" ValueChanged="@Handler"` |
| `Slider` | `Value="@x" OnAfterChange="@Handler"`（范围型需 `Range="true"` + `TValue="(double,double)"`） |
| `Pagination` | `OnChange="@Handler"` |
| `DatePicker` | `Value="@d" ValueChanged="@Handler"` |
| `InputNumber` / `Input` | `@bind-Value="field"` |
| `Select` | `Value="@v" ValueChanged="@Handler"` |

### 3.2 有些组件的"内置点击"也不可靠

`AntDesign.Tag` 的 `Checkable` 在插件运行时**有时点了没反应**（同一份代码在不同会话里表现不一致）。

**对策**：需要"标签样式 + 可靠点击"时，用 `Button` 套一层 Tag 皮肤：

```css
.ts-chip-btn.ant-btn { border-radius: 11px; height: 23px; padding: 0 10px; font-size: 12px; }
.ts-chip-btn-on.ant-btn  { background: #e6f4ff; border-color: #91caff; color: #0958d9; }
.ts-chip-btn-off.ant-btn { background: #fafafa; border-color: #f0f0f0; color: rgba(0,0,0,.35); text-decoration: line-through; }
```

```razor
<Button Size="@ButtonSize.Small" Class="@ChipClass(isOn)" OnClick="@(() => Toggle(name))">@name</Button>
```

> 结论：**凡是"点了要动"的东西，一律走 Button.OnClick**，视觉上再套皮肤。别赌组件内部的 `@onclick`。

### 3.3 邮箱启发式：中文/字母紧跟 `@表达式` 会被当字面量

```razor
<!-- ✗ 渲染成字面文本 "次数@SortArrow(x)" -->
<Button>次数@SortArrow(RankSortColumn.Count)</Button>

<!-- ✓ 用插值包起来 -->
<Button>@($"次数{SortArrow(RankSortColumn.Count)}")</Button>
```

原因：Razor 把 `word@word` 识别成邮箱地址。`¥@expr` 反而没问题（`¥` 不是词字符）。

### 3.4 其它语法小坑

- Razor 文件里引用项目类型需**全限定名**（如 `Alife.Foundation.AlifePath.StorageFolderPath`），否则报 `CS0246`；
- 字符串里别用中文引号包英文引号，容易踩 `RZ9986`（组件属性不支持混合内容）→ 把表达式抽成方法，属性里只写 `@Method(...)`。

---

## 4. 调试工具链（可直接复用的脚本）

放在任意临时目录，`$Port` 默认 9222。核心就三件事：**执行 JS、截图、发真实鼠标事件**。

### 4.1 执行 JS 并取回结果（最常用）

```powershell
param([Parameter(Mandatory=$true)][string]$Expression, [int]$Port = 9222)
$targets = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/list" -TimeoutSec 5
$page = $targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, [System.Threading.CancellationToken]::None).Wait(15000) | Out-Null
# 发送 {"id":1,"method":"Runtime.evaluate","params":{"expression":"...","returnByValue":true}}
# 读回 result.result.value
```

用它做**断言**，比看截图更可靠：

```javascript
(() => {
  const rows = [...document.querySelectorAll('.ts-rank-table tbody tr')];
  const sum = rows.reduce((s, tr) => s + (parseFloat(tr.querySelectorAll('td')[9].textContent) || 0), 0);
  return JSON.stringify({ rows: rows.length, sum: sum.toFixed(4) });
})()
```

### 4.2 截图

```powershell
# Page.captureScreenshot → base64 → 写文件
```

**能看图就一定要看图**：DOM 断言能保证"数据对不对"，只有截图能发现"排版丑不丑、有没有被遮挡、间距是否合理"。很多问题（元素被挤出视野、flex 塌陷、颜色对比不足）DOM 查不出来。

### 4.3 真实鼠标事件（拖拽 / 悬停 / 点击）

`Input.dispatchMouseEvent` 依次发 `mousePressed → mouseMoved → mouseReleased`。用于：

- 拖 `Slider` 的两个手柄；
- 验证"点击真的能触发"（`element.click()` 有时不够真实）；
- 悬停看 tooltip。

```powershell
# 典型顺序：先 scrollIntoView，等 500~800ms 让滚动稳定，再取 getBoundingClientRect，再发事件
```

> **坑**：`scrollIntoView()` 之后立刻取坐标，会拿到滚动前的旧坐标 → 点歪。务必 `Start-Sleep -Milliseconds 600` 再取。

### 4.4 OCR 兜底

模型/工具都读不到图时，可以用 WinRT `Windows.Media.Ocr`（`zh-Hans-CN` 引擎可用）把截图转文字。但既然能直接看图，就优先看图。

---

## 5. 布局与 CSS 踩坑清单

| 现象 | 原因 | 修法 |
|---|---|---|
| 窗口最大化后右侧一片空白 | 容器上有 `max-width: 1180px` 之类的限制 | 改成 `max-width: 100%` 或用 `width: 100%` |
| 表格表头与数字不对齐 | `.ts-rank-table th { text-align:left }`（0,1,1）压过了 `.ts-rank-num { text-align:right }`（0,1,0） | 提升具体性：`.ts-rank-table th.ts-rank-num { text-align:right }`，并给表头按钮 `padding:0` |
| 一整块内容挤到工具栏右侧 | flex 容器里塞了块级内容 | 拆出容器，让它单独成行 |
| 卡片头部一行塞太多，徽标/百分比被挤掉 | 单行 flex + `flex-wrap` 宽度不够 | 改成多行：`.ts-head-row { display:flex; flex-wrap:wrap; gap:6px }`，一行一件事 |
| 数字列宽度跳动、看着不对齐 | 比例字体 | `font-variant-numeric: tabular-nums` |
| 中间加粗的分隔线"像拖拽手柄" | 视觉语义误导 | 用两端渐隐的细线：`height:1px` + 水平渐变 |

**分区线的推荐写法**（试过虚线/中间加粗/椭圆裁切，最后选这个）：

```css
.ts-divider {
    height: 1px;
    margin: 22px 0 18px;
    background: linear-gradient(to right,
        rgba(0,0,0,0.02), rgba(0,0,0,0.12) 12%,
        rgba(0,0,0,0.12) 88%, rgba(0,0,0,0.02));
}
```

---

## 6. 数据正确性：三层验证缺一不可

页面"看起来对"和"算得对"是两件事。建议三层都做：

### 第一层：服务层断言（不碰 UI，跑得快）

在 `Demos/` 下写个控制台工程，构造临时数据 → 调服务 → 断言。**每次改动都跑一遍**，几十条断言秒级完成。

覆盖要点：
- 总量守恒（各分类之和 = 总额）；
- 边界情况（空数据、只有一条、超长文本、异常格式）；
- 排序/分页切片的边界；
- 新增的"目标值/阈值"逻辑（样本不足时应当返回 null，而不是硬凑）。

### 第二层：DOM 断言（验证绑定与渲染）

用 4.1 的脚本读取渲染后的数字，与预期比对。重点验：
- 表内可见行之和是否等于总额（差一行就会露馅）；
- 百分比是否合计 100%；
- 筛选后条数、分页页码、下拉选中项是否符合预期。

### 第三层：独立对账脚本（交叉验证）

**用另一套实现重算一遍**（不要复用产品代码），再和界面逐项比对。这一步能抓出"两边都自洽但口径不同"的问题。

真实案例：写了独立脚本按同样口径重算某天的数据（含节假日判定、分时定价、按字节分摊），结果与界面完全一致，才敢确认口径没跑偏。

### 一致性原则

> **同一屏上的所有数字必须能互相解释。**

比如"总花费 ¥0.1827"与"各分类分摊合计 ¥0.1826"（差 0.0001 是四舍五入）、"缓存 + 输入 = 输入合计"、"命中率 = 缓存 ÷ 输入合计"。做不到就是有 bug，不是"差不多"。

---

## 7. 交互设计经验

### 7.1 先把"作用域"分层，再摆控件

如果页面上有多种筛选（时间范围、区间滑块、分类过滤、下钻），**先问清楚：每个筛选影响哪些面板？**

推荐做法：把控件分组，让"谁影响谁"在布局上看得出来：

```
[时间范围]     → 影响：总览、走势图、诊断
   —— 分线 ——
[明细筛选]     → 影响：排行榜、明细列表
```

并在每个区块副标题里写明归属（"基于上方时间范围" / "按明细筛选后分摊"），再加一行"当前生效：范围 · 基础 N 条 · 明细 M 条"。

### 7.2 空状态绝不能是死路

**踩过的坑**：把筛选控件放在"有数据才渲染"的分支里 → 用户把条件收窄到 0 条 → 控件跟着消失 → 无法改回来。

规则：
- 所有"能改变查询条件"的控件，**必须在 0 条数据时仍然可见可用**；
- 空状态文案要区分"本来就没数据"和"被你自己筛没了"，后者要给出路（"把区间拉开或点重置"）。

### 7.3 文案要自解释

- 百分比写清基准：`占本期总花费` / `占输入 token`；
- 给结论就写门槛与依据：`变化 +19.9%，未达 20% 判定门槛（视为持平）`、`依据：本期 20 条 vs 前 1 天 20 条`；
- 估算要标明"非实测"：`线性外推，非实测`；
- 样本不足时**只陈述事实、不下结论**，不要为了"看起来有洞察"硬编一个目标值。

### 7.4 视觉上区分"可交互"与"信息"

- 可点的东西：胶囊/按钮/带下划线的表头 + `cursor:pointer`；
- 纯信息：纯文本或浅色标签；
- 分组线**不要**做得像拖拽手柄（别在中间加粗）。

---

## 8. 提交前检查清单

```
[ ] dotnet build 0 error，且没有新增 warning（CS9113/CS8602 都要清）
[ ] 服务层断言全绿
[ ] 干净 reload 后，DOM 断言确认新元素存在（排除"加载了旧 DLL"）
[ ] 截图确认排版：无横向溢出、无被遮挡、对齐正确
[ ] 窗口最大化 / 缩窄各看一次（max-width、换行）
[ ] 空状态、单条数据、超长文本各试一次
[ ] 所有筛选控件在 0 条数据时仍可用
[ ] 页面上的数字能互相解释（总额 = 各分类之和）
[ ] 文案里没有未解释的百分比/阈值
[ ] 死代码删除干净（未使用的字段/CSS/方法/参数）
```

---

## 9. 附录：最小可用调试脚本骨架

```powershell
# tools/cdp.ps1 —— 执行 JS 并回传结果
param([Parameter(Mandatory=$true)][string]$Expression, [int]$Port = 9222, [int]$TimeoutMs = 20000)
$ErrorActionPreference = "Stop"
$targets = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/list" -TimeoutSec 5
$page = $targets | Where-Object { $_.type -eq "page" } | Select-Object -First 1
$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).Wait($TimeoutMs) | Out-Null

function Send-Cdp([int]$id, [string]$method, $params) {
    $json = @{ id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 8 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).Wait($TimeoutMs) | Out-Null
    # …读回一帧 JSON，取 result.result.value
}

Send-Cdp 1 "Runtime.evaluate" @{ expression = $Expression; returnByValue = $true; awaitPromise = $true }
```

常用 JS 片段：

```javascript
// 列出所有卡片标题（确认渲染顺序）
[...document.querySelectorAll('.ts-card-title')].map(e => e.textContent.trim())

// 检查横向溢出
[...document.querySelectorAll('.ts-card, .ts-rank-table')].filter(e => e.scrollWidth > e.clientWidth + 2).length

// 点击某个组件（组件回调能被触发时可用）
[...document.querySelectorAll('button')].find(b => b.textContent.trim() === '按钮文字').click()

// 断言：某列合计
(() => { const rows = [...document.querySelectorAll('.ts-rank-table tbody tr')];
  return rows.reduce((s, tr) => s + (parseFloat(tr.querySelectorAll('td')[9].textContent) || 0), 0).toFixed(4); })()
```

---

## 10. 给后来者的三句话

1. **先让工具能看见再动手**：调试端口 + 截图 + DOM 断言，三件套不齐就别写 UI。
2. **每个"看起来正常"的数字都要有独立验证**：服务层断言、独立对账脚本、页面内自洽，三者任缺其一就会留下暗雷。
3. **文案是产品的一部分**：口径、门槛、依据写清楚，比多做一个图表更有价值。
