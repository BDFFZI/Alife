# AI 长期记忆

这是 Antigravity 的长期记忆文件。我会在这里记录重要的项目信息、用户偏好以及在对话中学习到的知识。

## 项目概览
- **项目名称**: Alife
- **主要语言**: C#, Python
- **功能**: 桌面宠物 (DeskPet) AI，包含语音识别/合成、视觉理解等功能。
- **STT 方案**: 已升级至 **Sherpa-ONNX + SenseVoiceSmall + Silero VAD**。
  - 特点：多语言支持（中英日韩等）、高精度、低延迟、加载极快。
  - 隐私保护：默认仅在检测到耳机时开启识别（`SpeechService.cs`）。
- **视觉方案 (Alife.Vision)**: **Qwen2.5-VL-3B-Instruct**（fp16, CUDA）
  - 原生中文支持，免费无Key，本地推理，约 4GB 显存。
  - 架构：Python Bridge 长驻进程（`qwen_vision_bridge.py`），C# 通过 `VisionAnalyzer.cs` 调用。
  - 功能：图像描述（caption）、视觉问答（query）。
  - Demo 项目：`Alife.Vision.Demo`（控制台交互）。
  - 后续计划：封装为 `VisionService.cs` 接入 `Alife.Function`。

## 用户偏好/反馈
- 用户对基于 Sherpa-ONNX 的新语音识别方案非常满意，反馈加载速度快、识别率高。
