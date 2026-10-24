using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.MessageFilter;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Auditory;

[Module("听觉能力",
    "为AI增加实时语音识别能力（监听麦克风并实时转文字），并显示可控制听觉状态的悬浮窗。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(AuditoryServiceUI))]
public partial class AuditoryService(
    Interactor<AuditoryService> interactor,
    MessageFilterService messageFilterService,
    global::Alife.Function.Auditory.IAuditoryModel auditoryModel,
    IAlifeApplication alifeApplication,
    PluginSystem pluginSystem,
    StorageSystem storageSystem,
    ILogger<AuditoryService> logger,
    AudioRecognitionService? audioRecognitionService = null) :
    ChatBehaviour,
    IConfigurable<AuditoryServiceConfig>
{
    public AuditoryServiceConfig Configuration { get; set; } = null!;
    public bool IsRunning { get; private set; }
    public bool IsListening { get; private set; }
    public bool IsTemporaryMicActive => temporaryMicHolds > 0;
    public event Action<bool>? IsListeningChanged;

    public Task ResetOverlayWindowAsync() => overlayWindow?.ResetWindowAsync() ?? Task.CompletedTask;

    public async Task<bool> BeginTemporaryMicAsync()
    {
        await temporaryMicGate.WaitAsync();
        try
        {
            if (temporaryMicHolds > 0)
            {
                temporaryMicHolds++;
                return false;
            }

            temporaryMicWasRunning = IsRunning;
            if (IsRunning == false)
                await StartRecordingAsync();
            temporaryMicHolds = 1;
            return true;
        }
        finally
        {
            temporaryMicGate.Release();
        }
    }

    public async Task EndTemporaryMicAsync()
    {
        await temporaryMicGate.WaitAsync();
        try
        {
            if (temporaryMicHolds == 0)
                return;

            temporaryMicHolds--;
            if (temporaryMicHolds > 0)
                return;

            if (temporaryMicWasRunning == false && IsRunning)
                StopRecording();
            temporaryMicWasRunning = false;
        }
        finally
        {
            temporaryMicGate.Release();
        }
    }

    void SetListeningState(bool value)
    {
        if (IsListening == value)
            return;

        IsListening = value;
        IsListeningChanged?.Invoke(value);
    }

    private partial bool StartPushToTalkMonitor();
    private partial void StopPushToTalkMonitor();

    AuditoryOverlayWindow? overlayWindow;
    readonly SemaphoreSlim temporaryMicGate = new(1, 1);
    int temporaryMicHolds;
    bool temporaryMicWasRunning;

    protected override async Task OnAwake()
    {
        try
        {
            overlayWindow = new AuditoryOverlayWindow(this, audioRecognitionService, alifeApplication, pluginSystem, storageSystem, logger);
            await overlayWindow.CreateAsync(Character, DestroyCancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "创建听觉控制悬浮窗失败。");
            if (overlayWindow != null)
            {
                await overlayWindow.DisposeAsync();
                overlayWindow = null;
            }
        }
    }

    protected override async Task OnStart()
    {
        //注册回复规则
        if (Configuration.ReplyRestrictedWords.Count != 0)
        {
            messageFilterService.AddMessageReplyRule(new MessageReplyRule {
                Name = nameof(AuditoryService),
                InputMatching = input => input.Contains(Interactor<AuditoryService>.GetMessageTag()),
                OutputMatching = output => Configuration.ReplyRestrictedWords.Any(tag => output.Contains(tag, StringComparison.OrdinalIgnoreCase)),
                CorrectionMessage = () =>
                    $"{nameof(AuditoryService)}消息必须用{string.Join("或", Configuration.ReplyRestrictedWords)}标签回复。如果不想发送消息，也请发送空标签。"
            }, DestroyCancellationToken);
        }

        //开始语言识别
        auditoryModel.Recognized += OnRecognized;
        if (StartPushToTalkMonitor() == false)
            await StartRecordingAsync();
    }

    protected override async Task OnDestroy()
    {
        if (overlayWindow != null)
        {
            await overlayWindow.DisposeAsync();
            overlayWindow = null;
        }

        StopPushToTalkMonitor();
        StopRecording();
        auditoryModel.Recognized -= OnRecognized;
    }

    void OnRecognized(string text)
    {
        interactor.Chat(text + "(消息为语音识别结果，错误率较高；建议也用语音功能回复)");
    }
}
