using System;
using System.IO;
using System.Threading.Tasks;
using SherpaOnnx;
using Alife.Framework;

namespace Alife.Function.Auditory.SenseVoice;

[Module("SenseVoice语音识别",
    "基于SenseVoice的本地语音识别引擎",
    defaultCategory: "Alife 官方/模型接入/听觉模型",
    EditorUI = typeof(SenseVoiceAuditoryModelUI))]
public class SenseVoiceAuditoryModel :
    ChatBehaviour,
    IConfigurable<SenseVoiceAuditoryModelConfig>,
    IAuditoryModel
{
    public static bool ModelsExists
    {
        get
        {
            string senseVoicePath = Path.Combine(AIModelUtility.ModelDownloader.ModelScopeModelPath, SenseVoiceId.Replace(".", "___"));
            string vadPath = Path.Combine(AIModelUtility.ModelDownloader.ModelScopeModelPath, VadId.Replace(".", "___"));
            return File.Exists(Path.Combine(senseVoicePath, "model.int8.onnx"))
                   && File.Exists(Path.Combine(vadPath, "silero_vad.onnx"));
        }
    }

    public SenseVoiceAuditoryModelConfig Configuration { get; set; } = null!;
    public event Action<string>? Recognized;

    public void AcceptWaveform(float[] samples)
    {
        var detector = vad;
        lock (detector)
        {
            detector.AcceptWaveform(samples);
            while (detector.IsEmpty() == false)
            {
                SpeechSegment segment = detector.Front();
                if (segment.Samples is { Length: > 0 })
                    ProcessSegment(segment.Samples);
                detector.Pop();
            }
        }

        void ProcessSegment(float[] samples)
        {
            using OfflineStream stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            recognizer.Decode(stream);

            string text = stream.Result.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            if (text == "。")
                return;
            Recognized?.Invoke(text);
        }
    }

    const string SenseVoiceId = "pengzhendong/sherpa-onnx-sense-voice-zh-en-ja-ko-yue";
    const string VadId = "pengzhendong/silero-vad";
    OfflineRecognizer recognizer = null!;
    VoiceActivityDetector vad = null!;

    protected override Task OnAwake()
    {
        string senseVoicePath = AIModelUtility.ModelDownloader.EnsureModelExisting(SenseVoiceId);
        string vadModelPath = AIModelUtility.ModelDownloader.EnsureModelExisting(VadId, "silero_vad.onnx");

        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(senseVoicePath, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = Configuration.Language;
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = Configuration.UseInverseTextNormalization ? 1 : 0;
        config.ModelConfig.Tokens = Path.Combine(senseVoicePath, "tokens.txt");
        config.ModelConfig.NumThreads = Configuration.NumThreads;
        config.ModelConfig.Debug = 0;
        recognizer = new OfflineRecognizer(config);

        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = Configuration.VadThreshold;
        vadConfig.SileroVad.MinSilenceDuration = Configuration.VadMinSilenceDuration;
        vadConfig.SileroVad.MinSpeechDuration = Configuration.VadMinSpeechDuration;
        vadConfig.SampleRate = 16000;
        vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);

        return Task.CompletedTask;
    }
    protected override Task OnDestroy()
    {
        recognizer.Dispose();
        vad.Dispose();

        return Task.CompletedTask;
    }
}
