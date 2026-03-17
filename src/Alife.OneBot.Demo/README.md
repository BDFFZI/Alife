# Alife OneBot Demo

本模块演示了如何将 Alife AI 接入 OneBot (QQ) 协议，实现多群组监控、智能回复及表情包互动。

## 核心功能

### 1. OneBot 集成
- **协议支持**：通过 WebSocket 连接 OneBot V11 服务。
- **智能群组管理**：支持特定群组监听，当被 @ 或提及关键词时自动唤醒 AI。
- **上下文感知**：AI 能识别发送者 ID、昵称及群组上下文。

### 2. 表情包系统 (Emote System)
- **统一寻址**：AI 通过 `<qimage file="名称" />` 标签即可调用表情。
- **渐进式查找**：支持“文件夹类别（随机）”、“精确文件名”及“模糊匹配”。
- **资产存放**：表情包管理在 `Storage/Emotes/` 目录下。

### 3. XML 解释器指令
- `<qchat target="..." type="...">内容</qchat>`：发送消息。
- `<qimage file="..." />`：发送表情/图片。

## 快速启动

1. **环境准备**：开启 OneBot 服务（如 NapCat），WS 监听 `ws://127.0.0.1:3001`。
2. **运行**：
   ```bash
   dotnet run
   ```
3. **资产**：确保 `bin` 目录下存在 `Storage/Emotes` 资源。
