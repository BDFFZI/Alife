# AGENTS.md —— Alife 开发规范

适用范围：`Sources/`、`Demos/`、`Tests/` 下的全部代码。本文件是所有开发会话的通用起点，规范内容以本文为准。

## 0. 最高优先：只写必要的代码

这是本项目最重要的规则，其余规则都是它的推论。

- 只解决当下明确的需求，不要揣测尚未被提出的需求。抽象、接口、扩展点、配置项只为“现在”写：要么已有真实调用方，要么分支数量已不可控（见 §2 开闭原则）。只有“以后可能会用到”这一条理由的，一律不写。
- 不写可能生效、不确定是否需要的“兜底代码”。如果某个分支、判空、重试、降级在正常情况下永远不成立，那它就不是健壮性，而是掩盖问题的噪音。
- 删掉比留下更好。一个没有被任何调用方使用的参数、方法、类、配置字段，就是负债。
- 代码量不是成果。不要用“考虑得很周全”的写法（大量判空、try-catch、日志、注释）来刷量。
- 不确定就用最直接的实现。经验不足时的“小巧思”通常会变成拖慢项目的枷锁。

## 1. 项目结构

```
Sources/
  Alife/                       核心库，无可执行入口
    Alife.Foundation           基础设施：日志、配置、上下文、镜像
    Alife.Framework            核心功能（M）：Models（ModuleAttribute、IConfigurable、Interactor）、Services（`*System`，DI 单例注册）、Utility
    Alife.PluginSystem         插件机制：PluginContext（C# 编译、依赖解析、AssemblyLoadContext 隔离加载）、PluginMarket（安装与提供）
    Alife.Application          跨平台应用层：Abstract（IAlifeApplication / IAlifeWindow / IAlifeProcess）、ClientUI（ModuleUIBase）、PluginEnvironment（NuGet / pip 安装器）
  Alife.Client/                宿主（P + V）
    Alife.Client               Blazor 共享 UI 库（AntDesign；Components/Pages 下 `*Page`，界面组件 `*UI`）
    Alife.Client.Windows       Windows 宿主：WinForms + WebView2 + Blazor Hybrid
    Alife.Client.Android       Android 宿主（含 .Shizuku）
  Alife.Function/              功能插件，一个目录一个插件：`[Module]` 模块类 + 可选 `*UI.razor`，manifest.json 声明版本与环境依赖
Demos/                         各功能独立示例工程
Tests/                         单元测试
Publish/                       发布脚本（client-windows / client-android / plugins + Inno 模板），产物默认输出 Desktop\Shared\Alife\Outputs
Directory.Build.props          全局设置：net10.0、Nullable enable
```

分层依赖（箭头指向被依赖方，由项目引用强制执行）：

```
Alife.Client.Windows / Alife.Client.Android
  → Alife.Client → Alife.Application → { Alife.Framework, Alife.PluginSystem } → Alife.Foundation
Alife.Function.*（插件）
  → Alife.Framework / Alife.PluginSystem（需要界面基类的插件可引用 Alife.Application）
```

- 插件之间允许直接项目引用，现有公共底座插件：`FunctionCaller`、`MessageFilter`、`Speech`、`Vision`、`AIModelUtility` 等；任何插件不得引用宿主与 `Alife.Client`。
- 运行链路：宿主 `Program` 组装 DI 容器 → `ModuleSystem` 发现并注册 `[Module]` 类 → `SystemManagement`、`CharacterWorkspace` 等页面依据 `ModuleAttribute`（`editorUI` / `globalUI`）渲染模块界面。
- 开发插件前端（Blazor UI）前先完整阅读 `.agents/skills/alife-blazor-plugin-ui/SKILL.md`。
- 发布新版本前先完整阅读 `.agents/skills/alife-publish/SKILL.md`。

## 2. 设计原则

### 开闭原则（关键）

当代码里出现 `if` / `switch` 分支，且分支数量不确定、可预见还会继续增加时，禁止用条件分支实现。应改为委托、抽象类、接口等，由外部实现原本的分支代码。

```csharp
// 错误：分支数量不确定，每加一种就要改这里
if (mode == GuideMode.Route) { ... }
else if (mode == GuideMode.Area) { ... }
else if (mode == GuideMode.Free) { ... }
```

判断依据很简单：**你能不能说出这个分支最多有几个**。说不出，就是违反开闭原则，必须重新设计。

### 单一职责与接口隔离

- 一段代码被分支，但某些场景下又完全不执行，说明它在那些场景中是多余实现，存在功能耦合。应进一步拆分，保证每个功能路径上执行的都是必要代码。
- 一个类只承担一件事。类名里出现“And”“Manager”“Helper”“Utility”通常是职责不清的信号。

### 高内聚低耦合 = 模块化

“模块化”不是逻辑上的功能独立，而是**随拆随删**：任何单一功能都能被单独拎出来，或删掉而不影响其他功能。

- 依赖必须是单向的、有明显层次隔离的。一个分支不允许影响另一个分支。
- 最小知识原则：只依赖你直接需要的东西，不要顺着调用链去拿别人的内部状态。
- 判断标准：如果一个功能无法在十分钟内单独迁移或删除，它的依赖关系就是错的。

### 分层（MVP / DDD 的 .NET 落地）

| 角色 | 本项目中的体现 |
| --- | --- |
| M（模型，核心功能） | `*System` / `*Service`：`Alife.Framework` 的 Services、各 `Alife.Function.*` 的功能服务 |
| V（视图，用户界面） | `*UI`：Razor 组件，只负责呈现与输入，不含业务逻辑 |
| P（主持人，驱动流程） | `*Page`：Razor 页面与宿主入口类，桥接并激活各模块，推动流程 |

规则：

- M 与 P 必须分离，V 不得直接依赖具体实现。
- 依赖只允许上层调用下层，不允许跨层调用，更不允许反向依赖。
- 分层由项目与程序集边界强制执行：跨层依赖通过项目引用暴露，未公开的成员留在 `internal`，用编译器约束代替自觉。
- 不要为了“分层”而分层。没到几十上百个模块的规模，就不要提前引入完整 DDD 的目录结构与分层仪式。

### 设计自检四问

每次交付前，逐条检查自己写的代码：

1. 是否存在数量不定的条件判断？→ 需要抽象（开闭原则）。
2. 是否存在结果恒定的条件判断？→ 存在多余实现，需要拆分。
3. 是否存在成段的复制粘贴？→ 需要封装复用，否则“一次调整、到处要改”。
4. 是否无法轻易迁移单个功能？→ 依赖关系有问题，需要解耦。

## 3. 明确禁止的写法

以下写法是在给项目挖坑，不是健壮性：

- **无意义的判空**：内部函数的参数由调用链保证非空时，不要写 `if (x == null) return;`。项目已全局启用 `Nullable`，可空性直接写在类型里（`T` 非空、`T?` 可空），装配契约用 `required` / `notnull`；`[NotNull]` 只用于类型表达不了的场景，而不是运行时兜底。参数不对劲就应该报错，而不是静默返回。
- **吞异常的 try-catch**：不要捕获异常后什么都不做，也不要只 `ex.Message` 丢失堆栈。要么不捕获，让异常自然上抛；要么在真正需要恢复的地方处理，并完整保留异常对象。
- **无意义的日志**：`Log("准备开始游戏")` 这类流水日志只消耗性能、淹没真正的错误。日志只在确有诊断需求时写，且要能被定位。
- **无意义的注释**：不要用注释把成员名翻译一遍，不要给每个属性写 `<summary>`。注释只解释用法、约束和特殊情况。
- **复制粘贴式编码**：出现整段复制粘贴时先封装。
- **顺手多写**：不要顺手加未要求的参数、未使用的配置项、未接入的分支、未使用的 using 与字段。
- **花里胡哨的“高级”写法**：不使用项目未采用的框架、模式、第三方库；不为展示技巧而写复杂代码。

## 4. 结构规范

让最常用的成员排在前面，让相关的成员排在一起。排序优先级从高到低：

1. 访问修饰符：公开 → 保护 → 私有。
2. 是否静态：静态 → 非静态。
3. 成员类型：数据定义（enum 等）→ 事件 → 字段 → 属性 → 函数。
4. 逻辑关系：生命周期回调、成对函数（`Open` / `Close`）相邻。

```csharp
public class Door
{
    public enum State { Closing, Opening, Opened }

    public event Action? Opened;
    public event Action? Closed;

    public State CurrentState => currentState;
    public Func<bool> OpenCondition => openCondition;

    public Task OpenAsync() { ... }
    public Task CloseAsync() { ... }

    private State currentState;
    private Func<bool> openCondition;
}
```

模块内分区：对外服务（属性、公开方法）放顶部，实现细节放尾部。

## 5. 命名规范

- 统一英文命名，不用汉语拼音；把驼峰拆开后必须能在翻译软件中得到正确的中文意思。
- 私有成员用驼峰命名法；非私有成员用帕斯卡命名法。
- 名字怕短不怕长，不要缩写；事实标准缩写（`Db`、`Id`、`Html`）除外，缩写时首字母大写、其余小写。禁止 `A`、`B`、`Ab` 这类无法解读的名字。
- 不用下划线，不用匈牙利命名法（不需要在名字里写类型）。
- 无意义名称禁止出现；名称必须能自注释。

| 类型 | 规则 | 示例 |
| --- | --- | --- |
| 接口 | 类名加 `I` 前缀 | `IConfigurable` |
| 泛型 | 类型名加 `T` 前缀 | `TConfig` |
| 函数 | 动作，主动语态 | `OpenDoor` |
| 事件 | 动作，被动语态；函数开头或中途触发用进行时，结束时用过去时 | `DoorOpening`、`DoorOpened` |
| 事件处理函数 | 对应事件名加 `On` 前缀 | `OnDoorOpened` |
| 字段 | 名词（可加形容词），被动语态，小驼峰 | `isDoorOpening` |
| 属性 | 与对应字段同名，公开时大写开头 | `IsDoorOpening` |

功能命名的后缀约定：

| 功能 | 约定 |
| --- | --- |
| 提供核心功能服务的类（M） | `+System` / `+Service` |
| 界面相关的类（V） | `+UI` |
| 驱动并链接模块的类（P） | `+Page` |
| 配置类 | `+Config` |
| 静态单例（仅容器注册不可行时） | `Instance` |

## 6. 实现规范

- 非常量字段不允许公开，用属性代替（属性可以验证操作、控制写入时机）。
- 依赖通过构造函数显式声明（本项目使用 `Microsoft.Extensions.DependencyInjection` 与主构造函数），不要在方法内部临时抓取依赖。
- 初始化集中在构造函数或明确的初始化入口，不要散落在各处。存在顺序依赖时，用容器注册顺序或显式的初始化调用把顺序写清楚。
- 能用 `await` / `async` 就不要用回调、事件、`ContinueWith` 拼异步；统一实现方式。
- 不要新增无用的抽象（多余的接口、基类、包装类）与控制反转仪式。

## 7. 可读性规范

- 省略默认修饰符（`private` 等），减少阅读量：写 `int value;` 而不是 `private int value;`。
- 区分“取反”和“判否”：判否写显式的比较 `if (isDoorOpened == false)`，取反逻辑才用 `!`（`bool isDoorClosing = !isDoorOpened;`）。不要用 `!` 表达判否。
- 不用 `var`；显式写出类型信息。目标类型明确的场合用 `List<int> b = new();` 这类语法糖。
- `if` 多层嵌套改为级联布局，提前 return，让各分支相互独立。
- 任何变量、函数都保持最小作用域，只放在真正使用它的环境中；局部函数优于私有方法。
- 优先使用新式 C# 语法（模式匹配、`?.`、`??`、元组、目标类型 `new`、集合表达式等）减少代码量，但要考虑代价：LINQ 可能带来 GC；本项目为性能敏感场景，热点路径（循环、每帧、序列化）以手写实现为准。

```csharp
// 错误：嵌套布局，代码块重合
if (a)
{
    if (b)
    {
        if (c) { ... }
    }
}

// 正确：级联布局，各代码块相互独立
if (a == false) return;
if (b == false) return;
if (c == false) return;
...
```

## 8. 其他原则

- 标准的目的是提高可读性与可维护性，不是面子工程。规则不适用时说明理由，不要为了“符合规范”增加无意义的形式。
- 及时修改代码。需求会变、结构会变，发现违反原则就立刻调整，不要偷懒；做好隔离后，再大的改动也只会烧在一个模块内。
- 没有框架是最好的框架。不要提前设计全局框架，先接到需求、实现功能、按需复用与调整。
- 不写 Demo 版代码，不做“以后要重构”的临时实现。
- 改动范围最小，不顺手修改无关代码。
