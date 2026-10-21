using System;
using Alife.Foundation;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
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
public partial class VisionService(
    XmlFunctionCaller functionService,
    Interactor<VisionService> interactor,
    IVisionModel? visionModel = null) :
    ChatBehaviour,
    IConfigurable<VisionServiceConfig>
{
    public VisionServiceConfig Configuration { get; set; } = null!;

    /// <summary>
    /// 截取指定窗口或全屏并进行视觉理解，将结果反馈给 AI。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("通过hwnd直接分析窗口画面")]
    public async Task AnalyseWindowImage(long hwnd, string prompt, int replyCharCount = 64)
    {
        string screenshotPath = await CaptureAsync(hwnd);

        //获取深度识别结果
        prompt += Configuration.AppendPrompt;
        if (hwnd == -1)
            prompt += $"（这是一张屏幕截图，当前焦点窗口为{GetActiveWindowTitle()}）";
        using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(DestroyCancellationToken);
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));
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
            string downloaded = $"{AlifePath.TempFolderPath}/vision_download_{Guid.NewGuid():N}.png";
            await AlifeUtility.DownloadFileAsync(pathOrUrl, downloaded);
            pathOrUrl = downloaded;
        }
        prompt += Configuration.AppendPrompt;
        using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(DestroyCancellationToken);
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));
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
    public async Task LookImage(string pathOrUrl, string prompt, int replyLength = 64)
    {
        await AnalyseImage(pathOrUrl, prompt, replyLength);
    }

    /// <summary>
    /// 截取指定窗口或全屏的屏幕截图，保存到本地并返回路径。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("不分析画面，仅保存窗口截图")]
    public async Task SaveWindowImage(long hwnd)
    {
        string screenshotPath = await CaptureAsync(hwnd);
        interactor.Poke($"""
                         截图已保存到：{screenshotPath}
                         """);
    }

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this)
        {
            Description =
                $"此服务让你拥有视觉感知能力，你可以通过<{nameof(QueryWindows)}>获取当前系统运行的窗口，然后传入到<{nameof(AnalyseWindowImage)}>中进行视觉分析。或则直接将图片链接或地址，传入到<{nameof(AnalyseImage)}>中分析"
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        return Task.CompletedTask;
    }
}
