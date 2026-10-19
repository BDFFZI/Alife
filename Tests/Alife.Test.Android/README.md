# Android window regression checks

Run the evdev parser tests with `dotnet test Tests/Alife.Test.Android/Alife.Test.Android.csproj`.
The DeskPet suite also checks DPI initialization, native bounds corrections, and cumulative gesture deltas.

For device integration, build the Android client in Debug with `-p:AlifeWindowProbe=true`, install
that APK, and run `python Tests/Alife.Test.Android/prepare-probe.py`. Push the generated page to
`/data/local/tmp/alife-window-probe.html`, then:

```sh
adb shell run-as com.alife.client mkdir -p files/window-probe
adb shell run-as com.alife.client cp /data/local/tmp/alife-window-probe.html files/window-probe/index.html
adb shell am start -n com.alife.client/com.alife.client.WindowProbeActivity
```

The probe creates two independent overlays using the real Android window implementation and current
DeskPet drag/resize scripts. Check browser geometry against `dumpsys window`, click counts,
physical cursor coordinates, dragging, resizing, input/IME, hide/show, Home, rotation and disposal.
The probe's `window.alifeWindow.postMessage({probe: ...})` commands cover `bounds`, `ignore`,
`hide`, `show`, `raise`, `cursor` and `dispose`. `facts()`, `events` and `messages` expose browser evidence.
Shizuku must be running and authorized for global input tests; `adb shell input` injects above evdev,
so it cannot by itself prove the global hardware-touch stream.

The probe Activity is excluded from normal builds. Install a normal Debug/Release APK after testing.
