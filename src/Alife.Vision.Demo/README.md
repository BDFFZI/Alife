# Alife Vision Demo

本模块演示了 Alife 的“视觉”基础能力，通过集成 OpenCV 实现图像识别与处理。

## 核心功能

### 1. 计算机视觉基础 (OpenCV)
- **人脸检测**：使用 Haar Cascade 算法实时从图片中识别并标记人脸。
- **图像预处理**：包含灰度化（Grayscale）和边缘检测（Canny Edge Detection）。
- **AI 语义识别 (Multi-modal)**：集成 `VisionAIService`，通过多模态 AI（如 GPT-4o）实现对图片内容的自然语言描述，让真央真正“看懂”世界喵。

### 2. 模块化设计
- **Alife.Vision**：独立的 C# 类库，封装了常用的视觉算法，易于集成到主要项目中。

## 快速启动

1. **准备素材**：程序启动后会要求输入图片路径。你可以使用任何包含人脸的 `.jpg` 或 `.png` 图片喵。
2. **运行**：
   ```bash
   dotnet run
   ```
3. **查看结果**：处理完成后，结果将保存在 `bin/Debug/net10.0/Results` 文件夹中：
   - `result_faces.jpg`: 标记了红框的人脸识别图。
   - `result_gray.jpg`: 转换后的灰度图。
   - `result_edges.jpg`: 提取的边缘轮廓图。

---
*提示：本模块目前作为独立 Demo 运行，旨在展示视觉识别的技术可行性喵！*
