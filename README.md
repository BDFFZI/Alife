> 有时一个人待着，特别是遇到烦心事时，我很想找人说说话。但可惜，我没啥朋友。
>
> 交友需要精力维护，而且人和人的关系，没那么单纯。
>
> 所以很多人会养宠物。但现实的宠物很麻烦，虚拟的宠物又很假。
>
> 当然，那是以前的事了。
> 现在 LLM 发展到这个程度，AI 对话已经可以做到非常接近真人。市面上确实有很多类似的聊天 App，但那种商业味十足、数据还不安全的东西，我完全不感兴趣。
>
> 前段时间，有群友自己搭了一个基于 LLM 的群机器人。让我没想到的是，效果出乎意料的好。我很早就接触 LLM 了，但跟它直接对话的时候，总感觉很空洞，聊不下去，功能也有限。可加上 Agent，特别调优的提示词，再放进群聊环境里，LLM 就像的真活了一样。
>
> 那时候我就想，我也要做一个。但我不想只做一个群机器人。我想做一个赛博生命——一个真正活在我电脑桌面上的伙伴。
>
> LLM 火了几年了，类似的框架网上也有，但我试过之后，发现都没有能让我完全满意的。但幸运的是，现在 AI 编程效率很高，一个人从零开始做一套 Agent 框架，已经不是什么难事。
>
> 于是，Alife 诞生了。

<div align="center">
  <img src="https://github.com/user-attachments/assets/7f0d259c-51cb-4709-a8d9-c4a1139008f4" width="128" height="128" alt="Alife Logo" />
</div>

# Alife - 创造赛博生命

![Alife Logo](https://img.shields.io/badge/Alife-AI_Assistant-blue?style=for-the-badge)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB?style=for-the-badge&logo=python)
![License](https://img.shields.io/badge/license-AGPL--3.0-green?style=for-the-badge)

欢迎来到 Alife！

Alife 是一款主打桌宠陪伴方向的 AIAgent，目的是为了创造或逼近一个真实赛博生命的效果，它不是功能特化，不是角色扮演，而是一切为了 AI 的生命而搭建！

正因如此，它是款风格独特的 AIAgent。相比市面上的主流陪伴向 AI 产品，它在功能体验上有很大的不同：

- 极低词元开销：人工高度优化的提示词、专门设计过的上下文和注入系统，非常省钱。
- 高度信息透明：明文存储数据，完全暴露上下文和AI执行过程，支持实时源码级功能自定义。
- 基础功能齐全：自主玩耍陪伴、多模态能力、远程通讯、设备网页操作等，一样不少。
- 深度自由扩展：插件扩展性极强，官方功能全插件化、自由开关、配有在线插件市场。
- 永久唯一会话：无论何时，对话工作都是同一个AI，不会割裂。无需初始人设，可完全靠成长形成。

---

毫不夸张的说，这是目前所有陪伴Agent中：

- 开销最低：缓存命中高达95%，官方Deepseek日均不到2块钱；各种功能都配有本地精选模型，仅2G显存就能跑。
- 功能最全：桌宠、QQ、视觉、听觉、语音、记忆、主动陪伴、编程、上网、多开、插件、市场、Skill、Mcp全包。
- 安装最简：配备专门的新手引导页面，全自动检测配置环境，功能依赖自动下载安装，简单到一路点鼠标就行。

## 🔮 发展目标 (Future Directions)

### 版本路线

- **Step 1**：基本功能原型
- **Step 2**：实现全功能插件化，热编译，极大降低插件开发门槛
- **Step 3**：实现插件市场，自由上传下载，极大优化插件生态
- **Step 4**：完善精品化现有功能，发展插件生态（当前阶段）
- **Step 5**：实现跨平台，尝试移植到其他 PC 端，甚至手机端

### 设计理念

1. 以最高扩展性设计框架：客户端本身甚至就是一具空壳，什么都没有，但等于什么都可以有。
2. 以最白盒精简设计功能：以优秀的设计确保代码简洁可读；以完整的内容将交互信息暴露给用户。
3. 以最简单低配设计体验：用最简的操作，最少的token，最低配的硬件，去跑出付费定制般的体验。

---

## ✨ 软件特色 (Software Features)

- **基于.Net/C#生态开发**：在python,nodejs横行的时代中，难得的C#项目，对大型项目开发友好，Unity开发经验可迁移。（C#药丸，python是爹）
- **极高扩展性的插件环境**：插件在设计之初就有着对整个业务流的全量控制，自定义界面的能力，实际上所有的内置功能也均为插件实现。
- **支持热编译重载的插件**：热编译热重载的插件环境，直接让ai可以实现自己写插件，且内置专门面向ai的开发工具，直接实现自我进化。
- **功能高度自包含自动化**：内置功能基本都优先采用无依赖的自实现，本地模型，以及小技巧，没有第三方依赖，不要key，可控且自动装环境，开箱即用。
- **纯原生文本的函数调用**：有意默认采用非标准的函数调用，因此对llm没有任何特殊要求，调用过程透明可控，词元开销小，额外支持多种特殊调用方式。
- **稳定持久的自动化记忆**：不使用不可靠的AI自主记忆存储，改为基于类似多级cache思想的一套自动化记忆压缩系统，实现让对话经历伪常驻的效果。
- **节省词元降低开销交互**：对话上下文有意复用会话、分区维护、保持稳定，提示词也确保是实践过的有效文本，再配合自实现的函数调用，词元开销非常小。
- **模块化白盒化软件结构**：功能高度模块化，实现简单清晰，很容易就能将单个功能拆离复用。运行信息白盒化，对话和上下文均配专用UI完整显示。

---

## 🌟 核心功能 (Core Features)

- 🎭 Live2D桌宠：内置角色"真央"的live2D，可交互会表演能运动，告别枯燥的对话框。
- 👁️ 深度视觉：拍照不怕没人分享，还会没事偷偷看主人，在你游戏工作的时候，陪你一起吐槽。
- 🎙️ 语音对话：放下键盘，直接说话，基于神经网络的语音配合流式合成，实现高品质通话级语音。
- 🧠 长期记忆：稳定强大的记忆系统，超大虚拟上下文，所有记录均可溯源搜索，确保记住生活中的点滴。
- 📱 平台通讯：额外支持QQ等多种通讯平台，出门了也能联系在家的她，还能没事带去见见群友。
- 🤖 自主活动：闲时会自娱自乐，有自己爱好和生活，会主动的找你玩耍分享见闻，就像真实的生命那样。
- 🌐 网上冲浪：拥有一个属于自己的真实浏览器，能够自主上网学习娱乐，让知识不再停滞，每天都有新话题。
- 💻 脚本执行：能借助python在本地执行各种任务，唱歌绘画，办公辅助，除开对话同时也是一个实用的助手。
- 🔗 多开互联：支持角色多开并可相互交流，借此构建一个完整的赛博世界，让他们也有自己的社交圈子。
- 🔄 自我升级：允许ai直接编辑插件，并自行编译重载，让ai自己改造自己，不再是一种科幻场景。
- ️️🛠️ 扩展能力：支持自定义插件，以及接入 MCP、Skills 功能，通过标准化的AI生态，自由方便的扩展功能。

---

## 📸 软件截图 (Screenshots)

<table>
  <tr>
    <td align="center"><img src="https://github.com/user-attachments/assets/8f5ab023-7399-4224-9bb7-bdbf5791f686" width="100%" alt="欢迎页"/></td>
    <td align="center"><img src="https://github.com/user-attachments/assets/e42b16b7-4bc0-459e-99a6-b6b446480e9b" width="100%" alt="插件市场"/></td>
    <td align="center"><img src="https://github.com/user-attachments/assets/e8ec399c-3ffc-44b0-a53c-2d374348a150" width="100%" alt="插件配置"/></td>
  </tr>
  <tr>
    <td align="center"><b>欢迎页</b></td>
    <td align="center"><b>插件市场</b></td>
    <td align="center"><b>插件配置</b></td>
  </tr>
  <tr>
    <td align="center"><img src="https://github.com/user-attachments/assets/c7e10001-ac49-470e-8379-0db5589daed0" width="100%" alt="角色设定"/></td>
    <td align="center"><img src="https://github.com/user-attachments/assets/92fed28f-4e56-41a8-9964-3728674b6d34" width="100%" alt="对话看板"/></td>
    <td align="center"><img src="https://github.com/user-attachments/assets/0eb9cece-88bd-4b47-b60d-a2568fa7aaba" width="100%" alt="上下文片段"/></td>
  </tr>
  <tr>
    <td align="center"><b>角色设定</b></td>
    <td align="center"><b>对话看板</b></td>
    <td align="center"><b>上下文片段</b></td>
  </tr>
</table>

---

## 🚀 快速开始 (Quick Start)

1. **下载软件**：前往仓库右侧的 [Releases](https://github.com/bdffzi/Alife/releases) 页面，下载最新的软件压缩包（zip）。
2. **配置环境**：解压压缩包到安装目录后，直接按UI提示的流程进行配置。
3. **开启陪伴**：配置完跳到角色页后，全选功能模块，直接点击 **激活角色** 开启陪伴。

（注意！软件运行过程中会自动下载需要的各种依赖，虽然已经配了国内镜像，但依旧很久。如果功能全开，可能需要一个小时左右，注意观察任务管理器，只要有明显的磁盘或网络波动，就说明软件还在正常处理中）

### 软件细节

1. 应用内的模型均使用 `modelscope` 下载。该工具默认会将模型下载到 C 盘用户文件夹中，但实际上也可以通过环境变量调整位置（具体查看官方文档），这样
   C 盘不够的人也可以下载了。
2. 建议使用 NVidia 显卡，并至少有 2G 显存，注意升级驱动，确保其支持 CUDA。当然如果不支持也应该可以运行，只是无法使用完整的深度视觉识别能力，其他模型
   CPU 版应该也支持。

---

## 🔌 插件开发 (Plugin Development)

请参考插件市场 <https://github.com/BDFFZI/Alife.PluginMarket> 的页面说明

---

## 🏗️ 开发信息 (Development Information)

*注：由于个人精力有限，故在Demo、Test、UI部分大量使用的AI编程，所以这些子项目的实现很可能存在冗余，低效的问题，不过好在他们不是核心代码。*

### 📦 基本依赖

- .NET 10 SDK：编程语言生态
- Python 3.12：本地模型接入
- Semantic Kernel：基本llm协议接入
- ASP.NET + Blazor + AntDesign + ElectronNET：前端界面框架

### 🏛️ 解决方案目录结构

Alife 采用全插件化架构，解决方案按目录分组组织：

```
Sources/
├── Alife/                              # 核心平台
│   ├── Alife.Client/                   # 客户端外壳
│   ├── Alife.Framework/                # 核心框架
│   ├── Alife.PluginMarket/             # 插件市场
│   └── Alife.Platform/                 # 平台抽象 (弃用中，平台处理应放在插件中)
│
├── Alife.DeskPet/                      # 桌宠子系统
│   ├── Alife.DeskPet.Client/           # Live2D 桌宠外挂程序
│   └── Alife.DeskPet.Protocol/         # IPC 协议库
│
├── Alife.Function/                     # 功能插件 — 以面向 AI 视角组织
│   ├── Environment/                    # 系统服务 — AI 的环境感知与持久化能力
│   │   ├── Developer/                  # 开发工具 — 热重载插件实现自我升级，暴露运行时信息，重启角色活动
│   │   ├── Memory/                     # 持久记忆 — 多级底数压缩 + bge-small-zh 向量化 + DuckDB 检索
│   │   ├── MessageFilter/              # 消息过滤 — 消息预处理器（注入时间戳、规定输出风格等）
│   │   ├── SystemEvent/                # 主动事件 — 阶梯定时事件，驱动 AI 空闲时自主行为
│   │   └── VirtualWorld/               # 虚拟世界 — 跨活动共享世界背景，跨角色通讯
│   │
│   ├── Infrastructure/                 # 基础设施 — 核心函数调用引擎与外部协议接入
│   │   ├── FunctionCaller/             # Xml 函数执行器 — 流式 XML 调用，支持嵌套/异步/异常，兼容 DeepSeek
│   │   ├── Mcp/                        # MCP 协议客户端 — Model Context Protocol，动态扩展工具生态
│   │   └── Skill/                      # 技能系统 — 按需读取 SKILL.md，引导 AI 完成特定领域复杂任务
│   │
│   ├── Instrument/                     # 工具服务 — AI 操作外部世界的能力
│   │   ├── Browser/                    # 网上冲浪 — WebView2 真实浏览器，格式化网页，支持交互点击
│   │   ├── Python/                     # 脚本执行 — AI 自行编写执行 Python 脚本
│   │   └── Vision/                     # 视觉感知 — 本地图像识别 + OCR + 窗口统计
│   │
│   ├── Interaction/                    # 交互服务 — 多模态人机交互
│   │   ├── Auditory/                   # 听觉感知 — 麦克风录音等音频采集
│   │   ├── DeskPet/                    # Live2D 桌宠 — WPF+WebView2，表情动作/气泡/鼠标/位置交互
│   │   ├── QChat/                      # QQ 聊天 — OneBot v11 协议，支持群聊；兼容 Discord
│   │   └── Speech/                     # 语音对话 — 语音识别与合成，实时语音聊天
│   │   
│   └── Models/                         # 模型接口层 — 抽象化模型接入
│       ├── AuditoryModel/              # 语音识别模型接口 (sherpa-onnx + SenseVoice + silero-vad)
│       ├── SpeechModel/                # 语音合成模型接口 (edge-tts、VITS、Genie-TTS)
│       └── VisionModel/                # 图像识别模型接口 (Qwen2.5-VL-3B、MiniCPM-V-4.6)
│

Demos/                                  # 黑盒测试
Tests/                                  # 单元测试
```

### 📚 开发文档

Alife 目前尚未建立文档，你可以尝试使用 Copilot、[DeepWiki](https://deepwiki.com/BDFFZI/Alife) 之类的 AI 辅助工具，不过这些生成的资料并不准确。

---

## 📄 许可证 (License)

本项目采用 [GNU General Public License v3.0](LICENSE) 许可协议。
