using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Alife.Function.Vision;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 一次采集周期内的画面快照。
/// 通过 Vision 插件的 WGC 截图能力捕获指定窗口或主显示器全屏（物理像素），
/// 支持按区域裁剪与像素读取，供验证与采集复用同一帧，减少重复截图开销。
/// 区域坐标为"采集源"坐标：全屏模式为屏幕坐标（原点 0,0）；窗口模式为窗口内容坐标（原点即窗口内容左上角）。
/// </summary>
public sealed class ScreenFrame : IDisposable
{
    readonly Bitmap bitmap;

    /// <summary>采集源内容原点在屏幕上的物理坐标（全屏为 0,0；窗口为其客户端区域原点）。</summary>
    public (int X, int Y) Origin { get; }

    /// <summary>捕获位图内容起点相对 Origin 的偏移（窗口捕获时含标题栏/边框；全屏为 0,0）。</summary>
    public (int X, int Y) CaptureOffset { get; }

    /// <summary>捕获位图物理像素宽度（全屏=主显示器物理分辨率宽）。</summary>
    public int Width => bitmap.Width;

    /// <summary>捕获位图物理像素高度（全屏=主显示器物理分辨率高）。</summary>
    public int Height => bitmap.Height;

    public ScreenFrame(Bitmap bitmap, (int X, int Y) origin = default, (int X, int Y) captureOffset = default)
    {
        this.bitmap = bitmap;
        Origin = origin;
        CaptureOffset = captureOffset;
    }

    /// <summary>捕获主显示器全屏。失败返回 null。</summary>
    public static async Task<ScreenFrame?> CaptureFullscreenAsync()
    {
        try
        {
            Bitmap bitmap = await WindowCaptureHelper.CaptureFullscreenAsync();
            Console.WriteLine($"[Companion] 全屏捕获 bitmap={bitmap.Width}x{bitmap.Height}");
            return new ScreenFrame(bitmap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companion] 屏幕捕获失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 按窗口标题子串捕获指定窗口。找到多个时取第一个匹配。
    /// 区域坐标以屏幕物理像素为基准（自动映射到窗口捕获位图坐标）。
    /// </summary>
    public static async Task<ScreenFrame?> CaptureForWindowAsync(string titleSubstring)
    {
        try
        {
            WindowInfo? window = WindowCaptureHelper.EnumerateWindows()
                .FirstOrDefault(w => w.Title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase));
            if (window == null)
            {
                Console.WriteLine($"[Companion] 未找到窗口: {titleSubstring}");
                return null;
            }

            Bitmap bitmap = await WindowCaptureHelper.CaptureWindowAsync(window.Handle);
            // 客户端区域原点（屏幕物理坐标）
            GetClientOrigin(window.Handle, out int originX, out int originY);
            // 客户端尺寸（物理像素）
            GetClientSize(window.Handle, out int clientW, out int clientH);
            // 捕获位图含标题栏与边框：左/右边框对称，上边框+标题栏为多出的高度
            int border = clientW > 0 ? Math.Max(0, (bitmap.Width - clientW) / 2) : 0;
            int captionOffset = clientH > 0 ? Math.Max(0, bitmap.Height - clientH - border) : 0;
            return new ScreenFrame(bitmap, (originX, originY), (border, captionOffset));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companion] 窗口捕获失败({titleSubstring}): {ex.Message}");
            return null;
        }
    }

    /// <summary>裁剪指定区域（矩形，自动夹紧到画面范围内）。</summary>
    public Bitmap? Crop(ScreenRegion region)
    {
        if (region == null || region.IsEmpty || bitmap == null)
            return null;

        int x = Math.Clamp(region.X - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y = Math.Clamp(region.Y - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        int width = Math.Min(region.Width, bitmap.Width - x);
        int height = Math.Min(region.Height, bitmap.Height - y);
        if (width <= 0 || height <= 0)
            return null;

        return bitmap.Clone(new Rectangle(x, y, width, height), bitmap.PixelFormat);
    }

    /// <summary>
    /// 读取区域中心点的像素颜色。
    /// </summary>
    public System.Drawing.Color? GetPixel(ScreenRegion region)
    {
        if (region == null || bitmap == null)
            return null;

        // 点模式用 (X,Y) 本身；否则用区域中心
        int px = region.IsPoint ? region.X : region.Center.X;
        int py = region.IsPoint ? region.Y : region.Center.Y;
        px = Math.Clamp(px - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        py = Math.Clamp(py - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        return bitmap.GetPixel(px, py);
    }

    /// <summary>
    /// 按区域采样像素颜色：矩形=区域内部所有像素；三角形=三角面内部所有像素。
    /// 返回覆盖的全部像素颜色，坐标自动夹紧到画面范围内。
    /// </summary>
    public List<System.Drawing.Color> GetShapePixels(ScreenRegion region)
    {
        var result = new List<System.Drawing.Color>();
        if (region == null || bitmap == null)
            return result;

        if (region.IsPoint)
        {
            System.Drawing.Color? p = GetPixel(region);
            if (p.HasValue)
                result.Add(p.Value);
            return result;
        }

        if (region.IsTriangle)
        {
            SampleTriangle(result, region);
            return result;
        }

        if (region.IsSector)
        {
            SampleSector(result, region);
            return result;
        }

        // 矩形内部采样
        if (region.IsEmpty)
            return result;
        int x0 = Math.Clamp(region.X - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y0 = Math.Clamp(region.Y - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        int x1 = Math.Clamp(region.X + region.Width - 1 - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y1 = Math.Clamp(region.Y + region.Height - 1 - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
                result.Add(bitmap.GetPixel(px, py));
        return result;
    }

    // 三角面内部采样：扫描线逐像素判断是否在三角形内
    void SampleTriangle(List<System.Drawing.Color> result, ScreenRegion region)
    {
        var pts = region.TrianglePoints;
        if (pts == null || pts.Count < 3)
            return;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (ScreenPoint p in pts)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        int x0 = Math.Clamp(minX - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y0 = Math.Clamp(minY - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        int x1 = Math.Clamp(maxX - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y1 = Math.Clamp(maxY - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);

        for (int py = y0; py <= y1; py++)
        {
            for (int px = x0; px <= x1; px++)
            {
                int sx = px + Origin.X - CaptureOffset.X;
                int sy = py + Origin.Y - CaptureOffset.Y;
                if (PointInTriangle(sx, sy, pts))
                    result.Add(bitmap.GetPixel(px, py));
            }
        }
    }

    // 扇面内部采样：扫描线逐像素判断是否在扇面内（圆心=X/Y，半径+角度）
    void SampleSector(List<System.Drawing.Color> result, ScreenRegion region)
    {
        int cx = region.X, cy = region.Y;
        int radius = Math.Max(1, region.Radius);
        double start = NormalizeAngle(region.StartAngle);
        double sweep = Math.Clamp(region.SweepAngle, 0, 360);

        int x0 = Math.Clamp(cx - radius - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y0 = Math.Clamp(cy - radius - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);
        int x1 = Math.Clamp(cx + radius - Origin.X + CaptureOffset.X, 0, bitmap.Width - 1);
        int y1 = Math.Clamp(cy + radius - Origin.Y + CaptureOffset.Y, 0, bitmap.Height - 1);

        for (int py = y0; py <= y1; py++)
        {
            for (int px = x0; px <= x1; px++)
            {
                int sx = px + Origin.X - CaptureOffset.X;
                int sy = py + Origin.Y - CaptureOffset.Y;
                double dx = sx - cx, dy = sy - cy;
                double distSq = dx * dx + dy * dy;
                if (distSq > (double)radius * radius)
                    continue;
                double ang = NormalizeAngle(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                if (AngleInSweep(ang, start, sweep))
                    result.Add(bitmap.GetPixel(px, py));
            }
        }
    }

    static double NormalizeAngle(double deg)
    {
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    static bool AngleInSweep(double ang, double start, double sweep)
    {
        if (sweep >= 360.0)
            return true;
        double end = start + sweep;
        if (end > 360.0)
            return ang >= start || ang < end - 360.0;
        return ang >= start && ang < end;
    }

    // 重心坐标法判断点是否在三角形内
    static bool PointInTriangle(int x, int y, List<ScreenPoint> pts)
    {
        (double ax, double ay) = (pts[0].X, pts[0].Y);
        (double bx, double by) = (pts[1].X, pts[1].Y);
        (double cx, double cy) = (pts[2].X, pts[2].Y);

        double v0x = cx - ax, v0y = cy - ay;
        double v1x = bx - ax, v1y = by - ay;
        double v2x = x - ax, v2y = y - ay;

        double dot00 = v0x * v0x + v0y * v0y;
        double dot01 = v0x * v1x + v0y * v1y;
        double dot02 = v0x * v2x + v0y * v2y;
        double dot11 = v1x * v1x + v1y * v1y;
        double dot12 = v1x * v2x + v1y * v2y;

        double inv = 1.0 / (dot00 * dot11 - dot01 * dot01);
        double u = (dot11 * dot02 - dot01 * dot12) * inv;
        double v = (dot00 * dot12 - dot01 * dot02) * inv;
        return u >= 0 && v >= 0 && u + v <= 1;
    }

    /// <summary>快照的物理像素尺寸。</summary>
    public Size Size => bitmap?.Size ?? default;

    public void Dispose()
    {
        bitmap?.Dispose();
    }

    // ---------- Win32: 获取窗口客户端区域原点（屏幕物理坐标） ----------

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    /// <summary>
    /// 确保进程按物理像素感知 DPI，使 Win32 坐标（ClientToScreen/GetWindowRect 等）
    /// 与 WGC 捕获的物理像素一致。失败时静默忽略。
    /// </summary>
    public static void EnsureProcessDpiAware()
    {
        try
        {
            SetProcessDpiAwarenessContext((IntPtr)(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            // ignored
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    static void GetClientOrigin(IntPtr hWnd, out int x, out int y)
    {
        x = 0;
        y = 0;
        try
        {
            if (GetClientRect(hWnd, out RECT client) && client.Right > 0 && client.Bottom > 0)
            {
                POINT p = new() { X = 0, Y = 0 };
                if (ClientToScreen(hWnd, ref p))
                {
                    x = p.X;
                    y = p.Y;
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    static void GetClientSize(IntPtr hWnd, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            if (GetClientRect(hWnd, out RECT client))
            {
                width = client.Right - client.Left;
                height = client.Bottom - client.Top;
            }
        }
        catch
        {
            // ignored
        }
    }
}
