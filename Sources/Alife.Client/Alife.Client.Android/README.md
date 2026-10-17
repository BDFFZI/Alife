# Android Client

Two projects with separate target frameworks (a single dual-TFM project would force
every restore, including plain backend builds and IDE design-time builds, to require
the android workload):

- `Alife.Client.Android` (`net10.0-android`): the native launcher, system WebView,
  foreground service and cross-application windows.
- `Alife.Client.Android.Backend` (`net10.0`): the Razor server, Alife backend,
  standard plugins and HTTP MCP. Published by the launcher build and embedded in
  the APK as `backend.zip`. `NativeHostCommand.cs` is compile-linked into the
  launcher so both sides share the host protocol without pulling the Web stack
  into the APK.

Shared Alife projects remain `net10.0`.

```powershell
dotnet build ..\Alife.Client.Android.Backend\Alife.Client.Android.Backend.csproj -c Release
```

```powershell
dotnet build Alife.Client.Android.csproj -c Release `
  -p:JavaSdkDirectory=C:/path/to/jdk `
  -p:AlifeAndroidClang=C:/path/to/android-ndk/clang.exe
```

On first launch, the foreground service downloads the pinned Android ARM64 SDK and native dependencies in `SdkPackages.json`, verifies SHA256 and installs them under `AlifePath.RuntimeFolderPath/DotNet`. Downloads honor `AlifeMirror`. The SDK is separate from the APK and reused on subsequent launches.

The service starts the embedded backend through the system linker, waits for its loopback HTTP endpoint, then opens the WebView. The launcher and backend share the existing Alife storage and runtime paths. Native window commands use a per-launch token; plugins continue to use `IAlifeClient` and `IAlifeWebWindow`.

Cross-application windows require Android's display-over-other-apps permission. Sending the application to the background keeps the backend and windows running. Removing the application from recent tasks stops the service, backend and windows; the service does not restart automatically.

The foreground service holds a partial wake lock while the backend runs and releases it when stopped. Some manufacturers additionally restrict foreground services while the screen is off. On the tested OnePlus device, enabling Alife's unrestricted background activity in the system app battery settings was required for MCP to remain responsive with the screen locked.

Use `HtmlWebWindowOptions` with `Url` set to a webpage address or a local HTML file path, or `RazorWebWindowOptions` with the required `RootComponent = typeof(App)`. Both inherit shared window properties from `WebWindowOptions`. HTML windows default to `about:blank`. Razor content is served by a separate backend page with no client routes, layout or initialization. `CrossApplication` creates a system overlay; show/hide, loading another URL or local HTML file and disposal use the same `IAlifeWebWindow` interface as Windows. Window sizes are pixels on Android. Android overlays do not provide desktop resize chrome or configurable z-order; plugins can implement their own web controls when needed.
