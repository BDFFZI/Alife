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
