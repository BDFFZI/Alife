# Android Client

Android 客户端复用共享的 `Alife.Client` Razor UI，并像 Windows 客户端一样使用
BlazorWebView（`Microsoft.AspNetCore.Components.WebView.Maui`）在**单进程**内渲染。
不再有独立的后端进程，也不再需要 HTTP/SignalR 桥接。

## 运行结构

```text
files/
  Alife/
    Storage/         角色数据、插件、设置
    Runtime/
      DotNet/        termux .NET SDK / Python（供插件 NuGet restore 使用）
      CompileRefs/   从 APK 解出的程序集引用缓存（按 APK 更新时间分版本）
      PluginContext/ 插件编译产物与 NuGet 解析结果
      PluginMarket/  插件市场缓存
    AutoStart        开机自启标记
```

- 主界面：`MainPage` 内嵌 `BlazorWebView`，加载共享的 `Components.Routes`。
- 插件：由进程内 Roslyn 编译。基础引用来自 APK 中的 `lib_*.dll.so`，首次编译时
  解出 PE 到 `Runtime/CompileRefs/{版本}/`，之后按文件路径复用。
- NuGet：插件声明 NuGet 依赖时，通过随 APK 嵌入的 `sdk-launcher`（linker64 +
  hostfxr）启动 termux .NET SDK 执行 `dotnet restore`。
- 保活：`KeepAliveService` 前台服务让系统不轻易回收进程（桌宠悬浮窗依赖）。

## 插件平台能力

插件源码在设备上编译时会注入 `ANDROID` 预处理符号，并引用 APK 内的全部程序集
（包括 `Mono.Android`）。因此插件可以直接使用 Android API：

```csharp
#if ANDROID
using Android.Media;

public sealed class AndroidSpeaker : ISpeechAudioPlayer
{
    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // 直接使用 MediaPlayer
    }
}
#endif
```

接口和实现都归插件自己所有，框架不需要为每个业务能力增加抽象。

## 构建

需要 JDK 17 和 Android NDK（用于编译 `Native/sdk-launcher.c`）。NDK 可显式传入：

```powershell
dotnet build Alife.Client.Android.csproj -c Release `
  -p:JavaSdkDirectory=C:/path/to/jdk `
  -p:AlifeAndroidClang=C:/path/to/android-ndk/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe
```

或设置 `ANDROID_NDK_ROOT` / `ALIFE_ANDROID_CLANG` 后再构建。

注意：Android 资源表在增量构建后可能残留旧 ID，修改资源或引用项目后建议
`dotnet build -t:Rebuild`，否则可能出现 `No view found for id ...` 之类的启动崩溃。

## 说明

- 通知权限在 Android 13+ 申请；被拒绝时前台服务仍可运行。
- MCP 由客户端提供传输实现。Android 当前未提供 ASP.NET 传输，因此 MCP 不启动。
- 客户端更新请安装新 APK；不支持应用内自升级。
- Android 不支持系统文件夹选择，存储路径固定为应用私有目录。

## 动态窗口与坐标

- `GetScreenSize()` / `GetCursorPosition()` 返回物理屏幕像素。窗口 `Bounds`、
  `window.screenX/screenY`、`screen.width/height` 采用 DPI 逻辑坐标；
  `innerWidth/innerHeight` 是 WebView 内容区的 CSS 像素。物理坐标除以 `GetDpiScale()` 后
  才能减去窗口位置得到注视/命中测试坐标。原生指针事件的 `screenX/Y` 已经是逻辑坐标，不二次换算。
- 每个窗口使用独立 `TYPE_APPLICATION_OVERLAY`，不依附主 Activity。Home 后仍显示，
  默认不抢键盘焦点、不拦截窗口外触摸；网页输入框聚焦时申请 IME 焦点。
  Android 的系统通知栏、权限页面等仍由系统决定层级，不能按 Win32 置顶规则覆盖。
- 未提供 Bounds 时给出有效的居中尺寸；窗口位置/大小限制在当前屏幕内，旋转后重新计算。
  `BoundsChanged` 回报实际内容区域，使 DeskPet 的注视原点与原生窗口一致。
- Shizuku 是必需环境。客户端以其 shell 身份读取触摸屏 `getevent`，处理主触点、硬件轴范围
  和显示旋转；即使窗口 `NotTouchable`，现有 DeskPet 光标轮询/网页命中测试仍可恢复触摸。
  不另加解锁控件。触摸没有鼠标悬停阶段，已经送给下层应用的一次按下不能追溯转交给悬浮窗。
- 页面桥只在 Launch 时开始导航，保证调用方能先订阅消息。支持 document-start 的 WebView
  使用 AndroidX 注入；旧 WebView 的本地 HTML 在资源读取时注入，避免 `OnPageStarted` 的时序竞争。
  旧 WebView 的 HTTP/HTTPS 窗口会明确提示升级系统 WebView。

设备探针及回归测试见 `Tests/Alife.Test.Android/README.md`。
