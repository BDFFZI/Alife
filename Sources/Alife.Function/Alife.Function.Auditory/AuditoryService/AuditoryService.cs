using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Auditory;

[Module("听觉能力",
    "为AI增加实时语音识别能力（麦克风、系统声音、音频文件），并显示可控制听觉状态的悬浮窗。",
    defaultCategory: "Alife 官方/交互方式",
    EditorUI = typeof(AuditoryServiceUI))]
public partial class AuditoryService(
    Interactor<AuditoryService> interactor,
    MessageFilterService messageFilterService,
    IAudioRecognizerProvider audioRecognizerProvider,
    IAlifeApplication alifeApplication,
    PluginSystem pluginSystem,
    StorageSystem storageSystem,
    ILogger<AuditoryService> logger,
    XmlFunctionCaller functionCaller) :
    ChatBehaviour,
    IConfigurable<AuditoryServiceConfig>
{
    public AuditoryServiceConfig Configuration { get; set; } = null!;
    public bool IsRunning { get; private set; }
    public bool IsListening { get; private set; }
    public bool IsTemporaryMicActive => temporaryMicHolds > 0;
    public bool IsListeningActive => listeningRecorder.IsRecording;
    public event Action<bool>? IsListeningChanged;

    public string PendingPushText
    {
        get
        {
            lock (pendingLock)
                return pendingPush.ToString();
        }
    }

    public double SecondsUntilNextPush
    {
        get
        {
            if (listeningRecorder.IsRecording == false || nextPushTime == DateTime.MinValue)
                return 0;
            double seconds = (nextPushTime - DateTime.Now).TotalSeconds;
            return seconds < 0 ? 0 : seconds;
        }
    }

    public Task ResetOverlayWindowAsync() => overlayWindow?.ResetWindowAsync() ?? Task.CompletedTask;

    [XmlFunction(FunctionMode.OneShot)]
    public async Task RecognizeAudioFile([Description("音频文件路径或链接")] string pathOrUrl)
    {
        string? tempDownload = null;
        try
        {
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                tempDownload = Path.Combine(AlifePath.TempFolderPath, $"audio_{Guid.NewGuid():N}");
                await AlifeUtility.DownloadFileAsync(pathOrUrl, tempDownload);
                pathOrUrl = tempDownload;
            }

            if (File.Exists(pathOrUrl) == false)
                throw new Exception($"文件不存在: {pathOrUrl}");

            float[] samples = await Task.Run(() => AudioDecoder.DecodeFileTo16kMonoFloat(pathOrUrl));

            StringBuilder result = new("【音频文件识别结果】\n");
            Action<string> onRecognized = text => result.AppendLine(text);
            oneShotRecognizer.Recognized += onRecognized;
            try
            {
                oneShotRecognizer.AcceptWaveform(samples, samples.Length);
                oneShotRecognizer.Flush();
            }
            finally
            {
                oneShotRecognizer.Recognized -= onRecognized;
            }

            interactor.Poke(result.ToString());
        }
        finally
        {
            if (tempDownload != null)
            {
                try
                {
                    File.Delete(tempDownload);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("开关系统声音监听（可用于听取游戏、视频等声音，实现陪看陪玩的效果）")]
    public async Task SetAudioListening(
        [Description("是否开启")] bool enabled)
    {
        if (enabled)
            await OpenAudioListening();
        else
            CloseAudioListening();
    }


    private partial bool StartPushToTalkMonitor();
    private partial void StopPushToTalkMonitor();

    AuditoryOverlayWindow? overlayWindow;
    IAudioRecognizer micRecognizer = null!;
    IAudioRecognizer oneShotRecognizer = null!;
    IAudioRecognizer listeningRecognizer = null!;
    SystemAudioRecorder listeningRecorder = null!;
    OccupationMarker? thinkingOccupationMarker;
    readonly Lock pendingLock = new();
    readonly StringBuilder pendingPush = new();
    DateTime nextPushTime;
    readonly SemaphoreSlim temporaryMicGate = new(1, 1);
    int temporaryMicHolds;
    bool temporaryMicWasRunning;

    protected override async Task OnAwake()
    {
        micRecognizer = audioRecognizerProvider.CreateAudioRecognizer();
        micRecognizer.Recognized += OnRecognized;

        oneShotRecognizer = audioRecognizerProvider.CreateAudioRecognizer();
        listeningRecognizer = audioRecognizerProvider.CreateAudioRecognizer();
        listeningRecognizer.Recognized += OnListeningRecognized;

        listeningRecorder = new SystemAudioRecorder();
        listeningRecorder.WaveformReady += OnListeningWaveform;

        XmlHandler xmlHandler = new(this) {
            Description = "此服务让你能够识别音频文件、监听系统声音，或控制麦克风识别。"
        };
        functionCaller.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        try
        {
            overlayWindow = new AuditoryOverlayWindow(this, alifeApplication, pluginSystem, storageSystem, logger);
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

        if (StartPushToTalkMonitor() == false)
            await StartRecordingAsync();
    }
    protected override Task OnUpdate()
    {
        if (listeningRecorder.IsRecording && DateTime.Now > nextPushTime)
        {
            TryFlushPendingPush();

            double seconds = Configuration.PushIntervalSeconds + (Random.Shared.NextSingle() * 2 - 1) * Configuration.PushJitterSeconds;
            nextPushTime = DateTime.Now + TimeSpan.FromSeconds(seconds);
        }

        return Task.CompletedTask;
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

        if (listeningRecorder.IsRecording)
            CloseAudioListening();

        listeningRecorder.WaveformReady -= OnListeningWaveform;
        listeningRecorder.Dispose();

        listeningRecognizer.Recognized -= OnListeningRecognized;
        listeningRecognizer.Dispose();
        oneShotRecognizer.Dispose();

        micRecognizer.Recognized -= OnRecognized;
        micRecognizer.Dispose();
    }

    void OnRecognized(string text)
    {
        interactor.Chat(text + "(消息为语音识别结果，错误率较高；建议也用语音功能回复)");
    }

    void OnListeningRecognized(string text)
    {
        lock (pendingLock)
            pendingPush.AppendLine(text);
    }

    void OnListeningWaveform(float[] samples, int count)
    {
        listeningRecognizer.AcceptWaveform(samples, count);
    }

    async Task OpenAudioListening()
    {
        if (listeningRecorder.IsRecording)
        {
            interactor.Poke("系统声音监听已开启");
            return;
        }

#if ANDROID
        await listeningRecorder.StartAsync("system", DestroyCancellationToken);
#else
        listeningRecorder.Start("system");
        await Task.CompletedTask;
#endif

        if (ChatBot.LanguageModel != null)
            thinkingOccupationMarker = ChatBot.LanguageModel.GetThinkingRequester().Rent("主动监听声音");
        interactor.Poke("系统声音监听被打开，接下来你将收到声音识别内容。");
    }

    void CloseAudioListening()
    {
        if (listeningRecorder.IsRecording == false)
        {
            interactor.Poke("系统声音监听未开启");
            return;
        }

        listeningRecorder.Stop();

        listeningRecognizer.Flush();
        TryFlushPendingPush();

        if (thinkingOccupationMarker != null)
        {
            ChatBot.LanguageModel?.GetThinkingRequester().Return(thinkingOccupationMarker);
            thinkingOccupationMarker = null;
        }
        interactor.Poke("已关闭系统声音监听");
    }

    void TryFlushPendingPush()
    {
        string content;
        lock (pendingLock)
        {
            if (pendingPush.Length == 0)
                return;
            content = pendingPush.ToString().Trim();
            pendingPush.Clear();
        }
        if (content.Length > 0)
            interactor.Poke("[声音监听片段]" + content + "(你可以空消息继续听，等听出完整有趣的内容后再和用户分享，避免频繁打扰)");
    }

    async Task<bool> BeginTemporaryMicAsync()
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

    async Task EndTemporaryMicAsync()
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
}