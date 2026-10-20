# Android Client

Android 客户端复用共享的 `Alife.Client` Razor UI，并像 Windows 客户端一样使用
BlazorWebView（`Microsoft.AspNetCore.Components.WebView.Maui`）在**单进程**内渲染。
不再有独立的后端进程，也不再需要 HTTP/SignalR 桥接。

## APK 签名

Debug 和 Release 构建统一使用仓库中的 `Properties/alife.keystore`，不同电脑构建的同包名 APK
可相互覆盖更新。别名为 `androiddebugkey`，密钥库和密钥密码均为 `android`。
证书随项目共享，因此所有持有项目的人都能签署更新包。统一签名不代表 APK 内容逐字节相同。
已安装应用若使用其他证书，不能直接被此证书签名的 APK 覆盖。

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

- 主 Activity 使用系统密度的 `1/2`（`Configuration.densityDpi`，按整数 DPI 取整）。
  Chromium 不随原生 Context 的密度改变，因此主页面和自定义 WebView 还使用 viewport 页面缩放。
  `GetDpiScale()`、窗口 Bounds 和事件屏幕坐标采用缩放后的逻辑密度；
  `devicePixelRatio` 仍是 Chromium 的原生值，实际 CSS 像素大小需再乘 `visualViewport.scale`。
  系统显示设置和其他应用不受影响。
- `GetScreenSize()` / `GetCursorPosition()` 返回物理屏幕像素。窗口 `Bounds`、
  `window.screenX/screenY`、`screen.width/height` 采用 DPI 逻辑坐标；
  `innerWidth/innerHeight` 是 WebView 内容区的 CSS 像素。物理坐标除以 `GetDpiScale()` 后
  才能减去窗口位置得到注视/命中测试坐标。页面桥将指针事件原有的 `screenX/Y` 按页面缩放比例换算，
  不使用变化中的窗口位置重新拼接，避免拖动时的时序误差。
- 每个窗口使用独立 `TYPE_APPLICATION_OVERLAY`，不依附主 Activity。Home 后仍显示，
  默认不抢键盘焦点、不拦截窗口外触摸；网页输入框聚焦时申请 IME 焦点。
  Android 的系统通知栏、权限页面等仍由系统决定层级，不能按 Win32 置顶规则覆盖。
- 未提供 Bounds 时给出有效的居中尺寸；窗口允许最多 3/4 越出屏幕，旋转后重新计算边界。
  `BoundsChanged` 回报实际内容区域，使 DeskPet 的注视原点与原生窗口一致。
- Shizuku 是必需环境。客户端以其 shell 身份读取触摸屏 `getevent`，处理主触点、硬件轴范围
  和显示旋转，供 DeskPet 注视及命中测试使用。
- `IAlifeWindow.SetMouseInputRegionsAsync` 接收客户区内的逻辑坐标矩形：null 为整窗输入，
  空数组为整窗穿透，非空数组为局部输入。Android 使用系统触摸区域，Windows 根据鼠标位置切换穿透。
  控件区域由调用方计算，窗口层不识别网页元素或桌宠标记；Android 第一次点击即可交互。
- 最后一次触摸位置作为持续光标位置，在窗口层向 WebView 合成原生鼠标悬停，移出窗口后清除。
  系统窗口外触摸或窗口失焦时同步清除网页编辑器焦点；不根据全局触摸坐标判断失焦，避免误伤软键盘输入。
- Android 12+ 限制不透明应用悬浮窗向其他应用穿透触摸。穿透窗口按同 UID 窗口数分配透明度，
  将叠加遮挡率控制在 0.79；单窗锁定时为 0.79，解锁后恢复 1。
  这是原生整窗透明度，与桌宠模型自身的悬停淡出独立；不修改系统全局触摸遮挡阈值。
- 页面桥只在 Launch 时开始导航，保证调用方能先订阅消息。支持 document-start 的 WebView
  使用 AndroidX 注入；旧 WebView 的本地 HTML 在资源读取时注入，避免 `OnPageStarted` 的时序竞争。
  旧 WebView 的 HTTP/HTTPS 窗口会明确提示升级系统 WebView。

设备探针及回归测试见 `Tests/Alife.Test.Android/README.md`。
