using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Alife.Function.GameCompanion.Screen;

/// <summary>
/// 文本识别服务：使用 Windows.Media.Ocr 对屏幕区域截图进行 OCR 识别。
/// 识别前做 3 倍放大提升识别率（保持原图，不做灰度/对比度处理）。
/// </summary>
public static class OcrService
{
    // OCR 引擎创建开销大且为原生对象，复用一个避免每次创建导致的泄漏与内存增长
    static OcrEngine? cachedEngine;

    /// <summary>
    /// 识别图片中的文本。
    /// </summary>
    /// <param name="image">要识别的图片（会被立即复制处理，不占用调用方资源）。</param>
    /// <returns>识别文本（去除了中文间冗余空格）。失败返回 null。</returns>
    public static async Task<string?> RecognizeAsync(Bitmap image, CancellationToken ct = default)
    {
        if (image == null)
            return null;

        string tempFile = Path.Combine(AlifePath.TempFolderPath, $"companion_ocr_{Guid.NewGuid():N}.png");
        try
        {
            image.Save(tempFile, System.Drawing.Imaging.ImageFormat.Png);
            return await RecognizeFileAsync(tempFile, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companion] OCR 识别失败: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    static async Task<string?> RecognizeFileAsync(string path, CancellationToken ct)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        // 3 倍高质量缩放，提升细小文字的识别率（保持原图色彩，不做灰度/对比度处理）
        BitmapTransform transform = new()
        {
            ScaledWidth = decoder.PixelWidth * 3,
            ScaledHeight = decoder.PixelHeight * 3,
            InterpolationMode = BitmapInterpolationMode.Cubic
        };
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            decoder.BitmapPixelFormat,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        OcrEngine engine = GetEngine();
        if (engine is null)
            return null;

        OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        string text = string.Join("\n", result.Lines.Select(line => line.Text));
        return Regex.Replace(text, @"(?<=[\u4e00-\u9fa5])\s+(?=[\u4e00-\u9fa5])", "");
    }

    /// <summary>获取（缓存）OCR 引擎；失败返回 null。</summary>
    static OcrEngine? GetEngine()
    {
        if (cachedEngine != null)
            return cachedEngine;
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            engine = language != null ? OcrEngine.TryCreateFromLanguage(language) : null;
        }
        cachedEngine = engine;
        return cachedEngine;
    }
}