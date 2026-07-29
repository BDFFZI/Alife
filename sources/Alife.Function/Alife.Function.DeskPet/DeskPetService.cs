using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.DeskPet;

[Module("桌宠交互",
    """
    将Live2D桌宠接入AI系统，实现表现力同步和互动反馈（仅支持Cubism 3及以上版本的live2D模型）
    可选模型下载地址：
    https://github.com/imuncle/live2d
    """,
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(DeskPetServiceUI))]
public class DeskPetService(
    XmlFunctionCaller functionService,
    IInteractor<DeskPetService> interactor) :
    ChatBehaviour,
    IConfigurable<DeskPetServiceConfig>
{
    public DeskPetServiceConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.Content)]
    [Description("显示一段气泡文本")]
    public async Task Speak(XmlExecutorContext context, [XmlContent] string content, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Closing:
                {
                    try
                    {
                        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                            await Task.Delay(TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()), cancellationToken);
                    }
                    finally
                    {
                        client.HideBubble();
                    }
                    break;
                }
                case CallMode.Content:
                {
                    content = content.Trim();
                    if (string.IsNullOrWhiteSpace(content))
                        break;
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < lastBubbleEndTime)
                        await Task.Delay(TimeSpan.FromMilliseconds(lastBubbleEndTime - DateTimeOffset.Now.ToUnixTimeMilliseconds()), cancellationToken);
                    client.ShowBubble(content);
                    lastBubbleEndTime = DateTimeOffset.Now.ToUnixTimeMilliseconds() + content.Length * 150;
                    break;
                }
            }
        }
        catch (OperationCanceledException) {}
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个表情（具体选项见附加说明）")]
    public void Expression(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client.SupportedExpressions.Contains(option) == false)
            throw new Exception("选项不存在");

        client.PlayExpression(option);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("表演一个动作（具体选项见附加说明）")]
    public void Motion(string option)
    {
        option = option.Trim();
        if (string.IsNullOrWhiteSpace(option))
            return;
        if (client.SupportedMotions.TryGetValue(option, out (string Group, int Index) motion) == false)
            throw new Exception("选项不存在");

        client.PlayMotion(motion.Group, motion.Index);
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取当前屏幕位置（使用后需等待结果返回）")]
    public async Task Position()
    {
        try
        {
            (double x, double y) = await client.GetPositionAsync();
            interactor.Poke($"当前位置: x={x}, y={y}");
        }
        catch (TimeoutException)
        {
            interactor.Poke("获取坐标超时");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("在屏幕上进行相对移动（注意！该移动方式为相对位置移动，使用前最好先确认当前位置）")]
    public async Task Move(double x = 0, double y = 0, float seconds = 1)
    {
        await client.MoveAsync(x, y, (int)(seconds * 1000));
        (x, y) = await client.GetPositionAsync();
        interactor.Poke($"移动成功，当前位置: x={x}, y={y}");
    }

    PetServer client = null!;
    long lastBubbleEndTime;
    bool lastStatus;

    protected override async Task OnAwake()
    {
        //启动桌宠客户端
        {
            string clientPath = Path.Combine(AlifePath.RuntimeFolderPath, "Alife.DeskPet.Client");
            if (!Directory.Exists(clientPath))
            {
                const string ZipUrl = "https://github.com/BDFFZI/Alife.OfficialPluginStorage/raw/refs/heads/main/Alife.DeskPet.Client/1.0.0.zip";
                await AlifeUtility.DownloadZipFileAsync(clientPath, ZipUrl);
            }

            string modelName = Configuration.ModelName;
            if (string.IsNullOrWhiteSpace(modelName))
                modelName = "Mao";
            client = new PetServer(clientPath, modelName);
        }

        //注册提示词
        {
            string supportedExpressionsDescription = string.Join(", ", client.SupportedExpressions);
            if (string.IsNullOrEmpty(supportedExpressionsDescription)) supportedExpressionsDescription = $"当前不支持<{nameof(Expression)}>功能";
            string supportedMotionsDescription = string.Join(", ", client.SupportedMotions.Keys);
            if (string.IsNullOrEmpty(supportedMotionsDescription)) supportedMotionsDescription = $"当前不支持<{nameof(Motion)}>功能";

            XmlHandler xmlHandler = new(this) {
                Description = "此服务让你获得一副交互性的Live2D身体。这是你主要的对外输出表情动作等外观信息的工具，需要积极使用。",
                Explanation = $"""
                               ## 支持选项
                               - 支持的 {nameof(Expression)} 选项：{supportedExpressionsDescription}
                               - 支持的 {nameof(Motion)} 选项：{supportedMotionsDescription}

                               ## 其他信息
                               - 当前屏幕分辨率：{GetResolution()}
                               """
            };
            functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        }
    }
    protected override async Task OnStart()
    {
        await client.WaitReadyAsync();
        client.OnInput += interactor.Chat;
        client.OnInteracted += text => interactor.Chat("交互：" + text);
    }
    protected override Task OnUpdate()
    {
        bool currentStatus = ChatBot.IsChatting;
        if (currentStatus != lastStatus)
        {
            lastStatus = currentStatus;
            client.SendStatus(currentStatus);
        }
        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        await client.DisposeAsync();
    }

    static (int Width, int Height) GetResolution()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            int width = GetDeviceCaps(hdc, Desktophorzres);
            int height = GetDeviceCaps(hdc, Desktopvertres);
            return (width, height);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
    const int Desktophorzres = 118;
    const int Desktopvertres = 117;
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
}
