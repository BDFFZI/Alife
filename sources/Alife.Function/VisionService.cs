using System.ComponentModel;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Vision;
using Alife;
using Microsoft.SemanticKernel;
using System.Net.Http;

namespace Alife.OfficialPlugins;

[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。")]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。（注意！分析系统并不准确，所以你需要配合结果，自己洞察出真正的实际情况）")]
public class VisionService : Plugin, IAsyncDisposable
{
    /// <summary>
    /// 截取屏幕并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction("look_screen")]
    [Description("截取当前整个屏幕并自动分析其内容。你可以附带问题，例如：<look_screen>屏幕上有什么？</look_screen>")]
    public async Task LookScreen(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing) return;

        string question = context.FullContent.Trim();
        if (string.IsNullOrWhiteSpace(question))
            question = "请用中文详细描述屏幕上的内容。";

        await EnsureInitializedAsync();

        string screenshotPath = CaptureScreen();
        try
        {
            string windowInfo = GetRunningWindowTitles();
            // 限制回复字长为 200 Token，提高响应速度
            string visionResult = await _analyzer.QueryAsync(screenshotPath, question);

            _chatBot.Poke($"[VisionService] 窗口信息：{windowInfo}\n视觉分析结果：{visionResult}");
        }
        finally
        {
            TryDeleteFile(screenshotPath);
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlFunction("look_image")]
    [Description(@"分析指定路径或 URL 的图片内容，用视觉模型理解后告知你。
用法示例：<look_image path=""xxx.jpg"">这张图里有什么？</look_image>")]
    public async Task LookImage(XmlExecutorContext context,
        [Description("图片文件的本地完整路径或网络 URL")] string path,
        [Description("你对这张图片的具体问题")] [XmlContent] string question)
    {
        if (context.CallMode != CallMode.Closing) return;

        string imagePath = path;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            _chatBot.Poke("[VisionService] 图片路径不能为空。");
            return;
        }

        string? tempDownloadedPath = null;
        try
        {
            // 处理网络图片
            if (imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                tempDownloadedPath = await DownloadImageAsync(imagePath);
                imagePath = tempDownloadedPath;
            }

            if (!File.Exists(imagePath))
            {
                _chatBot.Poke($"[VisionService] 图片路径无效或文件不存在：{imagePath}");
                return;
            }

            string finalQuestion = string.IsNullOrWhiteSpace(question) ? "请用中文详细描述这张图片的内容。" : question;

            await EnsureInitializedAsync();

            // 限制回复字长为 200 Token，提高响应速度
            string result = await _analyzer.QueryAsync(imagePath, finalQuestion);
            _chatBot.Poke($"[VisionService] 图片分析结果：{result}");
        }
        catch (Exception ex)
        {
            _chatBot.Poke($"[VisionService] 图片处理失败：{ex.Message}");
        }
        finally
        {
            if (tempDownloadedPath != null) TryDeleteFile(tempDownloadedPath);
        }
    }


    private readonly VisionAnalyzer _analyzer;
    private readonly StorageSystem _storageSystem;
    private static readonly HttpClient _httpClient = new();
    private ChatBot _chatBot = null!;
    private bool _initialized = false;

    public VisionService(StorageSystem storageSystem, InterpreterService interpreterService)
    {
        _storageSystem = storageSystem;
        _analyzer = new VisionAnalyzer();
        interpreterService.RegisterHandler(this);
    }
    public override Task AwakeAsync(AwakeContext context)
    {
        return EnsureInitializedAsync();
    }
    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatBot = chatActivity.ChatBot;
        return Task.CompletedTask;
    }

    private string CaptureScreen()
    {
        // 获取虚拟屏幕总尺寸（多显示器支持）
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            // 回退到主屏幕
            left = 0;
            top = 0;
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

        // --- 压缩图片分辨率 (优化：限制最大边长为 1024) ---
        const int maxSide = 512;
        string path = _storageSystem.GetTempPath("vision_screen.png");

        if (width > maxSide || height > maxSide)
        {
            float scale = Math.Min((float)maxSide / width, (float)maxSide / height);
            int newWidth = (int)(width * scale);
            int newHeight = (int)(height * scale);

            using var resized = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, newWidth, newHeight);
            }
            resized.Save(path, ImageFormat.Png);
        }
        else
        {
            bitmap.Save(path, ImageFormat.Png);
        }

        return path;
    }

    // ─────────────────────── Helpers ───────────────────────

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // 使用统一的环境库获取模型路径
        string modelsDir = PathEnvironment.ModelsPath;
        string modelPath = Path.Combine(modelsDir, "InternVL2_5-1B");
        await _analyzer.InitAsync(modelPath: modelPath, timeoutSeconds: 300, onLog: msg => Console.Write(msg));
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    private async Task<string> DownloadImageAsync(string url)
    {
        string tempPath = _storageSystem.GetTempPath($"download_{Guid.NewGuid():N}.png");
        var data = await _httpClient.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(tempPath, data);
        return tempPath;
    }

    public async ValueTask DisposeAsync()
    {
        _analyzer.Dispose();
        await Task.CompletedTask;
    }

    // ─────────────────────── Win32 PInvoke ───────────────────────

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private string GetRunningWindowTitles()
    {
        var titles = new List<string>();
        IntPtr foregroundWnd = GetForegroundWindow();

        EnumWindows((hWnd, lParam) => {
            if (IsWindowVisible(hWnd))
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var sb = new StringBuilder(length + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    {
                        if (hWnd == foregroundWnd)
                        {
                            title = $"(聚焦) {title}";
                        }
                        titles.Add(title);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return titles.Count > 0 ? string.Join(", ", titles) : "无可见窗口";
    }
}
