using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Alife.Abstractions;
using Alife.Interpreter;
using Alife.Vision;
using Alife;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

[Plugin("视觉感知", "让 AI 能够看到屏幕内容，理解图片，观察世界。")]
[Description("此服务让你拥有视觉感知能力：你可以截取屏幕画面并理解其内容，或者分析用户提供的图片。")]
public class VisionService : Plugin, IAsyncDisposable
{
    private readonly VisionAnalyzer _analyzer;
    private readonly StorageSystem _storageSystem;
    private ChatBot _chatBot = null!;
    private bool _initialized = false;

    public VisionService(StorageSystem storageSystem, InterpreterService interpreterService)
    {
        _storageSystem = storageSystem;
        _analyzer = new VisionAnalyzer();
        interpreterService.RegisterHandler(this);
    }

    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        _chatBot = chatActivity.ChatBot;
        await EnsureInitializedAsync();
    }

    // ─────────────────────── XML Handlers ───────────────────────

    /// <summary>
    /// 截取屏幕并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlHandler("look_screen")]
    [Description(@"截取当前屏幕画面，用视觉模型分析后将内容告知你。
适用场景：了解用户正在做什么、观察屏幕上的文字或图像内容。
用法示例：<look_screen>请描述屏幕上的内容</look_screen>
          <look_screen>屏幕上有错误信息吗？</look_screen>")]
    public async Task LookScreen(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing) return;

        string question = context.FullContent.Trim();
        if (string.IsNullOrWhiteSpace(question))
            question = "请用中文详细描述屏幕上的内容。";

        await EnsureInitializedAsync();

        string screenshotPath = CaptureScreen();
        try
        {
            string result = await _analyzer.QueryAsync(screenshotPath, question);
            _chatBot.Poke($"[VisionService] 屏幕内容如下：{result}");
        }
        finally
        {
            TryDeleteFile(screenshotPath);
        }
    }

    /// <summary>
    /// 分析指定路径的图片。
    /// </summary>
    [XmlHandler("look_image")]
    [Description(@"分析指定路径的图片文件，用视觉模型理解其内容后告知你。
用法示例：<look_image path=""C:\Users\user\photo.jpg"">这张图片里有什么人？</look_image>")]
    public async Task LookImage(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing) return;

        string imagePath = context.CallChain[^1].Attributes.TryGetValue("path", out var p) ? p : string.Empty;
        string question = context.FullContent.Trim();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            _chatBot.Poke("[VisionService] 图片路径无效或文件不存在。");
            return;
        }

        if (string.IsNullOrWhiteSpace(question))
            question = "请用中文详细描述这张图片的内容。";

        await EnsureInitializedAsync();

        string result = await _analyzer.QueryAsync(imagePath, question);
        _chatBot.Poke($"[VisionService] 图片分析结果：{result}");
    }

    // ─────────────────────── Screen Capture ───────────────────────

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

        string path = _storageSystem.GetTempPath("vision_screen.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    // ─────────────────────── Helpers ───────────────────────

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // 使用统一的环境库获取模型路径
        string modelsDir = PathEnvironment.ModelsPath;
        string qwenPath = Path.Combine(modelsDir, "Qwen2.5-VL-3B-Instruct");
        await _analyzer.InitAsync(modelPath: qwenPath, timeoutSeconds: 300);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { }
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
