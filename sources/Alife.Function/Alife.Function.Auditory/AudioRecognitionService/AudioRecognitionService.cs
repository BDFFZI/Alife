using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Auditory;

[Module("音频识别",
    "让AI能够主动调用语音识别来识别音频文件，或主动监听电脑声音来实现一起听视频之类的效果。",
    defaultCategory: "Alife 官方/实用工具",
    editorUI: typeof(AudioRecognitionServiceUI))]
public class AudioRecognitionService(
    XmlFunctionCaller functionCaller,
    Interactor<AudioRecognitionService> interactor,
    IAudioRecognizerProvider audioRecognizerProvider) :
    ChatBehaviour,
    IConfigurable<AudioRecognitionServiceConfig>
{
    public AudioRecognitionServiceConfig Configuration { get; set; } = null!;

    //以下属性暴露给 UI 监控
    public bool IsListeningActive => listeningRecorder.IsRecording;

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

    [XmlFunction(FunctionMode.OneShot)]
    public async Task RecognizeAudioFile([Description("音频文件路径或链接")] string pathOrUrl)
    {
        string? tempDownload = null;
        try
        {
            //在线音频链接：先下载到临时目录再解码
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

            //使用流式识别器（独立 VAD）一次性识别整段音频
            StringBuilder result = new StringBuilder("【音频文件识别结果】\n");
            Action<string> onRecognized = text => result.AppendLine(text);
            oneShotRecognizer.Recognized += onRecognized;
            oneShotRecognizer.AcceptWaveform(samples, samples.Length);
            oneShotRecognizer.Flush(); //输出末尾语音段
            oneShotRecognizer.Recognized -= onRecognized;
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
    [Description("主动进行声音监听（可用于听取系统中的游戏、视频等声音，实现陪看陪玩效果，不需要时应当关闭）")]
    public void SetAudioListening(
        [Description("是否开启")] bool enabled,
        [Description("system:扬声器,mic:麦克风")] string source = "system")
    {
        if (enabled)
            OpenAudioListening(source);
        else
            CloseAudioListening();
    }

    //文件识别
    IAudioRecognizer oneShotRecognizer = null!;
    //主动监听
    IAudioRecognizer listeningRecognizer = null!;
    SystemAudioRecorder listeningRecorder = null!; //复用对象化的系统录音器
    OccupationMarker? thinkingOccupationMarker;
    readonly Lock pendingLock = new(); //识别结果缓存（音频线程写，更新线程读）互斥
    readonly StringBuilder pendingPush = new(); //待合并推送的识别结果
    DateTime nextPushTime; //下一次推送的时间

    protected override Task OnAwake()
    {
        oneShotRecognizer = audioRecognizerProvider.CreateAudioRecognizer();
        listeningRecognizer = audioRecognizerProvider.CreateAudioRecognizer();
        listeningRecognizer.Recognized += OnListeningRecognized;

        listeningRecorder = new SystemAudioRecorder();
        listeningRecorder.WaveformReady += OnListeningWaveform;

        XmlHandler xmlHandler = new(this) {
            Description = "此服务让你能够识别音频文件，或主动监听系统或麦克风的声音。"
        };
        functionCaller.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        return Task.CompletedTask;
    }
    protected override Task OnDestroy()
    {
        if (listeningRecorder.IsRecording)
            CloseAudioListening();

        listeningRecorder.WaveformReady -= OnListeningWaveform;
        listeningRecorder.Dispose();
        listeningRecognizer.Recognized -= OnListeningRecognized;
        listeningRecognizer.Dispose();
        oneShotRecognizer.Dispose();
        return Task.CompletedTask;
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

    void OnListeningRecognized(string text)
    {
        //先缓存到 StringBuilder，由 OnUpdate 按配置间隔合并推送，避免高频消息打扰 AI
        lock (pendingLock)
            pendingPush.AppendLine(text);
    }
    void OnListeningWaveform(float[] samples, int count)
    {
        //系统录音器已统一转为 16k mono float，直接喂入识别器
        listeningRecognizer.AcceptWaveform(samples, count);
    }

    void OpenAudioListening(string source)
    {
        if (listeningRecorder.IsRecording)
        {
            interactor.Poke("声音监听已开启");
            return;
        }

        //对象化的系统录音器：负责采样与转格式，此处仅消费波形
        listeningRecorder.Start(source);

        thinkingOccupationMarker = ChatBot.LanguageModel.GetThinkingRequester().Rent("主动监听声音");
        interactor.Poke($"已开启{source}监听，系统将持续汇报听到的内容");
    }
    void CloseAudioListening()
    {
        if (listeningRecorder.IsRecording == false)
        {
            interactor.Poke("声音监听未开启");
            return;
        }

        listeningRecorder.Stop();

        listeningRecognizer.Flush();
        TryFlushPendingPush(); //关闭前把缓存的识别结果一并推送

        if (thinkingOccupationMarker != null)
        {
            ChatBot.LanguageModel.GetThinkingRequester().Return(thinkingOccupationMarker);
            thinkingOccupationMarker = null;
        }
        interactor.Poke("已关闭声音监听");
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
}