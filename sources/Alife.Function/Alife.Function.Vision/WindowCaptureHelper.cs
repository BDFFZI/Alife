using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Alife.Function.Vision;

// ============================================================
// COM 互操作接口定义
// ============================================================

/// <summary>
/// 通过 HWND / HMONITOR 创建 WGC capture item 的工厂接口。
/// IID: 3628E81B-3CAC-4C60-B7F4-23CE0E0C3356
/// </summary>
[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    [PreserveSig] int CreateForWindow(IntPtr hwnd, [In] ref Guid iid, out IntPtr ppv);
    [PreserveSig] int CreateForMonitor(IntPtr hmonitor, [In] ref Guid iid, out IntPtr ppv);
}

/// <summary>
/// 通过 WinRT IDirect3DSurface 取回底层 DXGI/D3D11 资源的接口。
/// IID: A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1
/// </summary>
[ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    [PreserveSig] int GetInterface([In] ref Guid iid, out IntPtr ppObj);
}

// ============================================================
// 窗口信息
// ============================================================

public class WindowInfo
{
    public IntPtr Handle { get; set; }
    public string Title { get; set; } = "";
    public bool IsMinimized { get; set; }
    public override string ToString() => $"[0x{Handle:X8}] {(IsMinimized ? "(最小化)" : "")} {Title}";
}

// ============================================================
// 工具类主体
// ============================================================

/// <summary>
/// 基于 Windows Graphics Capture API 的窗口/全屏截图工具。
/// 支持硬件加速内容（游戏、WebView 等），窗口被遮挡时也能正常截取。
/// 对于最小化窗口，使用"离屏恢复技巧"短暂恢复后再截图。
/// </summary>
public static class WindowCaptureHelper
{
    // -------- WinRT / COM IID --------
    private static readonly Guid IID_IGraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_ID3D11Texture2D =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    // -------- Win32 常量 --------
    private const int SW_SHOWNOACTIVATE = 4;// 显示但不激活（不抢焦点）
    private const int SW_MINIMIZE = 6;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // -------- P/Invoke --------
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);// 是否最小化
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [DllImport("d3d11.dll", PreserveSig = false)]
    static extern void CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
    static extern void RoGetActivationFactory(
        IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int WindowsDeleteString(IntPtr hstring);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);

    // -------- 共享 D3D11 设备（延迟初始化）--------
    private static ID3D11Device? _device;
    private static ID3D11DeviceContext? _context;
    private static IDirect3DDevice? _winRtDevice;
    private static readonly object _initLock = new();

    private static void EnsureDevice()
    {
        if (_device != null) return;
        lock (_initLock)
        {
            if (_device != null) return;

            // 创建 D3D11 硬件设备，启用 BGRA 支持（WGC 要求）
            var featureLevels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            D3D11.D3D11CreateDevice(
            null, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out _device!, out _, out _context!);

            // 获取 IDXGIDevice 并转换为 WinRT IDirect3DDevice
            using IDXGIDevice dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr winRtPtr);
            _winRtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winRtPtr);
            Marshal.Release(winRtPtr);

            Console.WriteLine("[WGC] D3D11 设备初始化完成。");
        }
    }

    // ============================================================
    // 公开 API
    // ============================================================

    /// <summary>枚举当前所有可截图的顶层可见窗口。</summary>
    public static List<WindowInfo> EnumerateWindows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((hwnd, _) => {
            if (!IsWindowVisible(hwnd)) return true;
            int len = GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;
            list.Add(new WindowInfo {
                Handle = hwnd,
                Title = title,
                IsMinimized = IsIconic(hwnd)
            });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>枚举当前所有显示器句柄。</summary>
    public static List<IntPtr> EnumerateMonitors()
    {
        var list = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hm, _, _, _) => {
            list.Add(hm);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// 截取指定窗口画面。
    /// 若窗口最小化，会短暂"离屏恢复"（SW_SHOWNOACTIVATE）后再截图，截图后立即重新最小化。
    /// </summary>
    public static async Task<Bitmap> CaptureWindowAsync(IntPtr hwnd, int timeoutMs = 5000)
    {
        EnsureDevice();

        bool wasMinimized = IsIconic(hwnd);
        if (wasMinimized)
        {
            Console.WriteLine("[WGC] 检测到最小化窗口，执行离屏恢复...");
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);// 不抢焦点地恢复
            await Task.Delay(200);// 等 GPU 重渲一帧
        }

        try
        {
            var item = CreateItemForWindow(hwnd);
            return await CaptureItemAsync(item, timeoutMs);
        }
        finally
        {
            if (wasMinimized)
            {
                ShowWindow(hwnd, SW_MINIMIZE);
                Console.WriteLine("[WGC] 已重新最小化。");
            }
        }
    }

    /// <summary>截取全屏（指定显示器，默认主显示器）。</summary>
    public static async Task<Bitmap> CaptureFullscreenAsync(IntPtr hMonitor = default, int timeoutMs = 5000)
    {
        EnsureDevice();

        if (hMonitor == default)
        {
            var monitors = EnumerateMonitors();
            if (monitors.Count == 0) throw new InvalidOperationException("未找到任何显示器");
            hMonitor = monitors[0];
        }

        var item = CreateItemForMonitor(hMonitor);
        return await CaptureItemAsync(item, timeoutMs);
    }

    // ============================================================
    // 核心捕获逻辑
    // ============================================================

    private static async Task<Bitmap> CaptureItemAsync(GraphicsCaptureItem item, int timeoutMs)
    {
        SizeInt32 size = item.Size;

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
        _winRtDevice!,
        DirectXPixelFormat.B8G8R8A8UIntNormalized,
        2,// 双缓冲
        size);

        using var session = framePool.CreateCaptureSession(item);

        // 禁用 Windows 11+ 截图边框指示器 & 光标（需要 SDK >= 22621）
        // IsBorderRequired / IsCursorCaptureEnabled 仅在较新 SDK 版本中存在，
        // 通过反射按需调用，编译期不依赖具体版本
        TrySetSessionProp(session, "IsBorderRequired", false);
        TrySetSessionProp(session, "IsCursorCaptureEnabled", false);

        void TrySetSessionProp(GraphicsCaptureSession s, string propName, bool value)
        {
            try
            {
                var prop = s.GetType().GetProperty(propName);
                if (prop != null && prop.CanWrite) prop.SetValue(s, value);
            }
            catch {}
        }

        // 等待第一帧
        var tcs = new TaskCompletionSource<Direct3D11CaptureFrame?>(
        TaskCreationOptions.RunContinuationsAsynchronously);

        framePool.FrameArrived += (pool, _) => {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(pool.TryGetNextFrame());
        };

        session.StartCapture();

        using var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        using Direct3D11CaptureFrame? frame = await tcs.Task;

        if (frame == null)
            throw new TimeoutException("WGC 捕获超时，未收到帧");

        var bmp = ConvertFrameToBitmap(frame, size);

        // 释放 GraphicsCaptureItem 的 RCW 引用（session/framePool 已由 using 释放）
        try { Marshal.ReleaseComObject(item); } catch { }

        return bmp;
    }

    private static unsafe Bitmap ConvertFrameToBitmap(Direct3D11CaptureFrame frame, SizeInt32 size)
    {
        // 1. 取 WinRT surface 的 IUnknown 指针
        IntPtr surfaceAbi = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface);

        // 2. QI -> IDirect3DDxgiInterfaceAccess
        Guid dxgiGuid = IID_IDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(surfaceAbi, dxgiGuid, out IntPtr dxgiAccessPtr);
        Marshal.Release(surfaceAbi);
        Marshal.ThrowExceptionForHR(hr);

        // 3. 通过 IDirect3DDxgiInterfaceAccess.GetInterface() 取 ID3D11Texture2D
        var dxgiAccess = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr);
        Marshal.Release(dxgiAccessPtr);

        Guid texGuid = IID_ID3D11Texture2D;
        hr = dxgiAccess.GetInterface(ref texGuid, out IntPtr texPtr);
        Marshal.ThrowExceptionForHR(hr);

        using ID3D11Texture2D sourceTexture = new ID3D11Texture2D(texPtr);

        // 4. 创建 Staging 纹理（CPU 可读）
        Texture2DDescription desc = sourceTexture.Description;
        var stagingDesc = new Texture2DDescription {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,// Vortice 3.8 改名 CPUAccessFlags
            MiscFlags = ResourceOptionFlags.None
        };
        using ID3D11Texture2D staging = _device!.CreateTexture2D(stagingDesc);

        // 5. GPU -> Staging 拷贝
        _context!.CopyResource(staging, sourceTexture);

        // 6. Map 到 CPU 内存，复制像素
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read);
        try
        {
            Bitmap bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            BitmapData bmpData = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

            int rowBytes = size.Width * 4;
            for (int y = 0; y < size.Height; y++)
            {
                void* src = (void*)(mapped.DataPointer + (nint)(y * mapped.RowPitch));
                void* dst = (void*)(bmpData.Scan0 + (nint)(y * bmpData.Stride));
                NativeMemory.Copy(src, dst, (nuint)rowBytes);
            }

            bmp.UnlockBits(bmpData);
            return bmp;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    // ============================================================
    // WGC GraphicsCaptureItem 工厂
    // ============================================================

    private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GetCaptureItemInterop();
        Guid itemGuid = IID_IGraphicsCaptureItem;
        int hr = interop.CreateForWindow(hwnd, ref itemGuid, out IntPtr itemPtr);
        Marshal.ThrowExceptionForHR(hr);
        var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return item;
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmonitor)
    {
        var interop = GetCaptureItemInterop();
        Guid itemGuid = IID_IGraphicsCaptureItem;
        int hr = interop.CreateForMonitor(hmonitor, ref itemGuid, out IntPtr itemPtr);
        Marshal.ThrowExceptionForHR(hr);
        var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        Marshal.Release(itemPtr);
        return item;
    }

    private static IGraphicsCaptureItemInterop GetCaptureItemInterop()
    {
        string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        WindowsCreateString(className, className.Length, out IntPtr hString);
        try
        {
            Guid interopGuid = IID_IGraphicsCaptureItemInterop;
            RoGetActivationFactory(
            hString,
            ref interopGuid,
            out IntPtr factoryPtr);
            var factory = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.ReleaseComObject(factory);   // 还清 GetObjectForIUnknown 增加的 RCW 引用
            Marshal.Release(factoryPtr);
            return factory;
        }
        finally
        {
            WindowsDeleteString(hString);
        }
    }
}
