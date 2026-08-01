using System;
using Alife.Foundation;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Vision;

public record VisionServiceConfig
{
    //对图片的附加提示词
    public string AppendPrompt { get; set; } = "（请精简的描述一下图片大体内容，避免输出过多的文本，提高分析速度）";
}

[Module("视觉感知",
    "让 AI 能够看到屏幕内容，理解图片，观察世界。",
    defaultCategory: "Alife 官方/实用工具",
    EditorUI = typeof(VisionServiceUI))]
public class VisionService(
    XmlFunctionCaller functionService,
    IInteractor<VisionService> interactor,
    IVisionModel? visionModel = null) :
    ChatBehaviour,
    IConfigurable<VisionServiceConfig>
{
    public VisionServiceConfig Configuration { get; set; } = null!;

    /// <summary>
    /// 获取当前可以截取的所有可用窗口的列表，供 AI 选择截屏目标。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查询当前可见窗口，及其hwnd和焦点")]
    public void QueryWindows()
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
    }

    /// <summary>
    /// 截取指定窗口或全屏并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("通过hwnd直接分析窗口画面")]
    public async Task AnalyseWindowImage(long hwnd, string prompt, int replyCharCount = 64)
    {
        //验证窗口句柄是否存在
        if (hwnd != -1)
        {
            var windows = WindowCaptureHelper.EnumerateWindows();
            bool exists = windows.Any(w => w.Handle.ToInt64() == hwnd);
            if (!exists) throw new Exception("hwnd不存在，请先查询");
        }

        //截取目标画面
        string screenshotPath = Path.Combine(AlifePath.TempFolderPath, $"vision_capture_{DateTime.Now.Ticks}.png");
        {
            using var bmp = hwnd == -1
                ? await WindowCaptureHelper.CaptureFullscreenAsync()
                : await WindowCaptureHelper.CaptureWindowAsync(new IntPtr(hwnd));
            bmp.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        //获取深度识别结果
        prompt += Configuration.AppendPrompt;
        if (hwnd == -1)
            prompt += $"（这是一张屏幕截图，当前焦点窗口为{GetActiveWindowTitle()}）" + Configuration.AppendPrompt;

        CancellationTokenSource cancellationTokenSource = new(30000);
        string deepVisionResult = visionModel != null
            ? $"{await visionModel.QueryAsync(
                screenshotPath,
                prompt,
                replyCharCount,
                cancellationToken: cancellationTokenSource.Token)}"
            : "未开启";

        if (hwnd == -1)
        {
            interactor.Poke($"""
                             屏幕分析结果
                             深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                             """);
        }
        else
        {
            interactor.Poke($"""
                             窗口分析结果
                             文字识别：{await OcrAsync(screenshotPath)}
                             深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                             """);
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    public async Task AnalyseImage(
        string pathOrUrl, string prompt, int replyLength = 64)
    {
        // 处理网络图片
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string downloaded = $"{AlifePath.TempFolderPath}/vision_download.png";
            await AlifeUtility.DownloadFileAsync(pathOrUrl, downloaded);
            pathOrUrl = downloaded;
        }

        prompt += Configuration.AppendPrompt;

        CancellationTokenSource cancellationTokenSource = new(30000);
        string deepVisionResult = visionModel != null
            ? await visionModel.QueryAsync(
                pathOrUrl,
                prompt,
                replyLength,
                cancellationToken: cancellationTokenSource.Token)
            : "未开启";

        interactor.Poke($"""
                         图片分析结果
                         文字识别：{await OcrAsync(pathOrUrl)}
                         深度视觉：{deepVisionResult}（内容不一定准确仅供参考）
                         """);
    }

    /// <summary>
    /// 截取指定窗口或全屏的屏幕截图，保存到本地并返回路径。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("不分析画面，仅保存窗口截图")]
    public async Task SaveWindowImage(long hwnd)
    {
        //验证窗口句柄是否存在
        if (hwnd != -1)
        {
            var windows = WindowCaptureHelper.EnumerateWindows();
            bool exists = windows.Any(w => w.Handle.ToInt64() == hwnd);
            if (!exists) throw new Exception("hwnd不存在，请先查询");
        }

        //截取目标画面
        string screenshotPath = Path.Combine(AlifePath.TempFolderPath, $"vision_capture_{DateTime.Now.Ticks}.png");
        {
            using var bmp = hwnd == -1
                ? await WindowCaptureHelper.CaptureFullscreenAsync()
                : await WindowCaptureHelper.CaptureWindowAsync(new IntPtr(hwnd));
            bmp.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
        }

        interactor.Poke($"""
                         截图已保存到：{screenshotPath}
                         """);
    }

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = $"此服务让你拥有视觉感知能力，你可以通过<{nameof(QueryWindows)}>获取当前系统运行的窗口，然后传入到<{nameof(AnalyseWindowImage)}>中进行视觉分析。或则直接将图片链接或地址，传入到<{nameof(AnalyseImage)}>中分析"
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        
        return Task.CompletedTask;
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
            BitmapTransform transform = new() {
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
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
