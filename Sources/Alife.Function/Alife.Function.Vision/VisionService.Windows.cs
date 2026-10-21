#if WINDOWS
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Alife.Function.Vision;

public partial class VisionService
{
    /// <summary>
    /// 获取当前可以截取的所有可用窗口的列表，供 AI 选择截屏目标。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查询当前可见窗口，及其hwnd和焦点")]
    public async Task QueryWindows()
    {
        var windows = WindowCaptureHelper.EnumerateWindows()
            .Where(w => !string.IsNullOrWhiteSpace(w.Title))
            .ToList();
        interactor.Poke($"""
                         【可见窗口列表】
                         {string.Join("\n", windows.Select(info => $"hwnd: {info.Handle.ToInt64()} | 标题: {info.Title}"))}
                         hwnd: -1 | 直接查看全屏内容
                         【当前用户聚焦】
                         {GetActiveWindowTitle()}
                         """);
        await Task.CompletedTask;
    }

    async Task<string> CaptureAsync(long hwnd)
    {
        if (hwnd != -1 && !WindowCaptureHelper.EnumerateWindows().Any(w => w.Handle.ToInt64() == hwnd))
            throw new ArgumentException("hwnd不存在，请先查询", nameof(hwnd));
        string path = Path.Combine(AlifePath.TempFolderPath, $"vision_capture_{Guid.NewGuid():N}.png");
        using var bitmap = hwnd == -1
            ? await WindowCaptureHelper.CaptureFullscreenAsync()
            : await WindowCaptureHelper.CaptureWindowAsync(new IntPtr(hwnd));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    static string GetActiveWindowTitle()
    {
        const int NChars = 256;
        StringBuilder buff = new(NChars);
        IntPtr handle = GetForegroundWindow();
        if (GetWindowText(handle, buff, NChars) > 0)
        {
            return buff.ToString();
        }
        return "Unknown";
    }

    static async Task<string> OcrAsync(string path)
    {
        if (File.Exists(path) == false)
        {
            return string.Empty;
        }
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

            // 优化：进行 2 倍高质量缩放预处理，显著提升细小文字的识别率
            BitmapTransform transform = new()
            {
                ScaledWidth = decoder.PixelWidth * 2,
                ScaledHeight = decoder.PixelHeight * 2,
                InterpolationMode = BitmapInterpolationMode.Cubic
            };
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                decoder.BitmapPixelFormat,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                return "OCR 引擎初始化失败";
            }
            OcrResult result = await engine.RecognizeAsync(bitmap);
            string text = string.Join("\n", result.Lines.Select(line => line.Text));
            // 优化：去除中文之间的冗余空格
            return Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+(?=[\u4e00-\u9fa5])", "");
        }
        catch (Exception ex)
        {
            return $"OCR 识别出错: {ex.Message}";
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
#endif
