using System;
using System.IO;
using System.Threading.Tasks;
using SherpaOnnx;
using Alife.Framework;
using Alife.Function.AIModelUtility;

namespace Alife.Function.Auditory.SenseVoice;

[Module("SenseVoice语音识别",
    "基于SenseVoice的本地语音识别引擎",
    defaultCategory: "Alife 官方/模型接入/听觉模型",
    EditorUI = typeof(SenseVoiceAuditoryModelUI))]
public class SenseVoiceAuditoryModel(IAlifeApplication alifeApplication) :
    ChatBehaviour,
    IConfigurable<SenseVoiceAuditoryModelConfig>,
    global::Alife.Function.AIModelUtility.IAuditoryModel,
    global::Alife.Function.AIModelUtility.IAudioRecognizerProvider
{
    public static bool ModelsExists
    {
        get
        {
            string senseVoicePath = Path.Combine(ModelDownloader.ModelScopeModelPath, SenseVoiceId.Replace(".", "___"));
            string vadPath = Path.Combine(ModelDownloader.ModelScopeModelPath, VadId.Replace(".", "___"));
            return File.Exists(Path.Combine(senseVoicePath, "model.int8.onnx"))
                   && File.Exists(Path.Combine(senseVoicePath, "tokens.txt"))
                   && File.Exists(Path.Combine(vadPath, "silero_vad.onnx"));
        }
    }

    public SenseVoiceAuditoryModelConfig Configuration { get; set; } = null!;

    public event Action<string>? Recognized { add => realtimeRecognizer.Recognized += value; remove => realtimeRecognizer.Recognized -= value; }
    public void AcceptWaveform(float[] samples, int length) => realtimeRecognizer.AcceptWaveform(samples, length);
    
    public global::Alife.Function.AIModelUtility.IAudioRecognizer CreateAudioRecognizer() => new SenseVoiceAudioRecognizer(recognizer, vadConfig);

    const string SenseVoiceId = "pengzhendong/sherpa-onnx-sense-voice-zh-en-ja-ko-yue";
    const string VadId = "pengzhendong/silero-vad";
    OfflineRecognizer recognizer = null!;
    VadModelConfig vadConfig = new();
    SenseVoiceAudioRecognizer realtimeRecognizer = null!;

    protected override async Task OnAwake()
    {
        await ModelDownloader.EnsureModelExistingAsync(alifeApplication, SenseVoiceId, "tokens.txt", DestroyCancellationToken);
        string modelFile = await ModelDownloader.EnsureModelExistingAsync(alifeApplication, SenseVoiceId, "model.int8.onnx", DestroyCancellationToken);
        string senseVoicePath = Path.GetDirectoryName(modelFile)!;
        string vadModelPath = await ModelDownloader.EnsureModelExistingAsync(alifeApplication, VadId, "silero_vad.onnx", DestroyCancellationToken);

        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(senseVoicePath, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = Configuration.Language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = Configuration.UseInverseTextNormalization ? 1 : 0;
        config.ModelConfig.Tokens = Path.Combine(senseVoicePath, "tokens.txt");
        config.ModelConfig.NumThreads = Configuration.NumThreads;
        config.ModelConfig.Debug = 0;
        recognizer = new OfflineRecognizer(config);

        vadConfig = new VadModelConfig();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = Configuration.VadThreshold;
        vadConfig.SileroVad.MinSilenceDuration = Configuration.VadMinSilenceDuration;
        vadConfig.SileroVad.MinSpeechDuration = Configuration.VadMinSpeechDuration;
        vadConfig.SampleRate = 16000;

        realtimeRecognizer = new SenseVoiceAudioRecognizer(recognizer, vadConfig);
    }
    protected override Task OnDestroy()
    {
        realtimeRecognizer.Dispose();
        recognizer.Dispose();
        return Task.CompletedTask;
    }
}

