# Alife Pet Demo & Interaction 

本模块演示了真央 (Mao) 桌宠的 AI 交互能力，包含表情控制、动作触发及底层参数调节。

## 核心功能

### 1. 桌面宠物交互 (Mao)
- **Live2D 渲染**：支持表情、动作切换与平滑视线追踪。
- **实时物理反馈**：支持 **晃动 (Shake)**、**连击 (Combo)**、**旋转 (Rotate)** 及 **位移 (Move)**。
- **气泡消息**：支持文字和表情的动态交互气泡。

### 2. AI 姿态控制
- **参数解耦**：AI 可以通过 `<pet_param>` 直接调节 Live2D 参数（如歪头）。
- **平滑过渡**：内置 Tween 引擎，确保参数变化自然不生硬。
- **自动回正**：预设动作完成后自动恢复 idle 状态。

### 3. XML 解释器指令
- `<pet_bubble>内容</pet_bubble>`：控制说话。
- `<pet_exp>情绪</pet_exp>`：切换表情。
- `<pet_mtn>动作</pet_mtn>`：执行动作。
- `<pet_param name="..." value="..." duration="..." />`：调节参数。
- `<pet_move x="..." y="..." />`：控制位移。

## 快速启动

1. **运行 UI 端**：进入 `src/Alife.Pet` 运行 `dotnet run` 开启窗口。
2. **运行 Demo 端**：在此目录下运行：
   ```bash
   dotnet run
   ```
3. **交互**：在控制台输入文字即可与真央聊天。
