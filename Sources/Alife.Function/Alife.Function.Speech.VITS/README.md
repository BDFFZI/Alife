# VITS（sherpa-onnx）

Windows、Android 使用同一套本地 ONNX 推理，不依赖 Python、PyTorch 或 Shizuku。

在配置页指定模型目录；留空时使用 `Runtime/VITS/onnx`。目录需包含：

- `alife-paimon.onnx`：Alife 原 VITS 模型的 ONNX 导出，保留原说话人 ID（默认 551，派蒙）。
- `tokens.txt`、`lexicon.txt`：与导出模型配套的音素表、词典。
- `speakers_list.txt`：可选，用于界面选择角色；也兼容旧版 `Runtime/VITS/speakers_list.txt`。

MetaTourAI 的 `Assets/AssetBundles/AIModels/VITS` 已有前三个文件，复制时去掉 Unity 的 `.bytes` 后缀。不能把旧的 `.pth` 文件直接改名为 `.onnx`。模型较大，不纳入插件源码，也不会再自动下载旧的 PyTorch ZIP。

当前前端沿用 MetaTourAI：中文标点规范化、数字逐位读出、拉丁字母分隔。词典主要面向中文，日语及复杂数字读法不保证与旧 Python 前端完全一致。

模型首次合成时加载，默认 CPU 2 线程（可设置 1–4）。合成串行执行；取消在原生生成回调边界生效，加载模型和单个生成片段不能立即中断。停止角色时等待正在执行的调用结束再释放模型。音频先写临时文件，成功后才进入缓存，失败或取消不留下半成品缓存。

Android 客户端通过 NuGet 打包 sherpa-onnx 1.13.5 的 ARM64 原生库。插件编译、依赖加载沿用现有机制，无共享框架改动。
