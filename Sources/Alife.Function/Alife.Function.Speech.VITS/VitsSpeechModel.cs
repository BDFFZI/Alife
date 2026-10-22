using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Foundation;
using Alife.Function.AIModelUtility;
using SherpaOnnx;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech.VITS;

[Module("VITS语音合成", "基于 sherpa-onnx 的跨平台本地离线语音合成", defaultCategory: "Alife 官方/模型接入/语音模型", EditorUI = typeof(VitsSpeechModelUI))]
public class VitsSpeechModel(ILogger<VitsSpeechModel> logger) : ChatBehaviour, ISpeechModel, IConfigurable<VitsSpeechModelConfig>
{
    public static string RuntimeFolder => Path.Combine(AlifePath.RuntimeFolderPath, "VITS");
    public static string GetModelDirectory(VitsSpeechModelConfig config) =>
        string.IsNullOrWhiteSpace(config.ModelDirectory) ? Path.Combine(RuntimeFolder, "onnx") : Path.GetFullPath(config.ModelDirectory);
    public static bool IsModelReady(VitsSpeechModelConfig config) =>
        File.Exists(Path.Combine(GetModelDirectory(config), "alife-paimon.onnx")) &&
        File.Exists(Path.Combine(GetModelDirectory(config), "tokens.txt")) &&
        File.Exists(Path.Combine(GetModelDirectory(config), "lexicon.txt"));

    public VitsSpeechModelConfig Configuration { get; set; } = null!;
    readonly SemaphoreSlim gate = new(1, 1);
    OfflineTts? synthesizer;
    EngineSettings? loadedSettings;
    DateTime loadedModelTime;
    bool disposed;

    record EngineSettings(string Directory, float NoiseScale, float NoiseScaleW, float LengthScale, int WorkerThreads);

    protected override async Task OnAwake()
    {
        CancellationToken ct = DestroyCancellationToken;
        await gate.WaitAsync(ct);
        try
        {
            await EnsureModelAsync(ct);
            EngineSettings settings = GetSettings();
            var model = new FileInfo(Path.Combine(settings.Directory, "alife-paimon.onnx"));
            await Task.Run(() => {
                ct.ThrowIfCancellationRequested();
                InitializeModel(settings, model);
                ct.ThrowIfCancellationRequested();
                if (Configuration.SpeakerId >= synthesizer!.NumSpeakers)
                    throw new ArgumentOutOfRangeException(nameof(Configuration.SpeakerId), $"说话人 ID 应为 0–{synthesizer.NumSpeakers - 1}。");
            }, CancellationToken.None);
        }
        catch
        {
            // Awake 失败时框架不会调用 OnDestroy，释放已创建的原生模型。
            synthesizer?.Dispose();
            synthesizer = null;
            throw;
        }
        finally { gate.Release(); }
    }
    protected override async Task OnDestroy()
    {
        await gate.WaitAsync();
        try
        {
            disposed = true;
            synthesizer?.Dispose();
            synthesizer = null;
        }
        finally { gate.Release(); }
    }

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Length > 4000) throw new ArgumentException("合成文字不能超过 4000 字符。", nameof(text));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, DestroyCancellationToken);
        CancellationToken ct = linked.Token;
        await gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsureModelAsync(ct);
            EngineSettings settings = GetSettings();
            int speaker = Configuration.SpeakerId;
            string normalized = NormalizeText(text);
            var model = new FileInfo(Path.Combine(settings.Directory, "alife-paimon.onnx"));
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                text = normalized, speaker, settings, model.Length, model.LastWriteTimeUtc, engine = "sherpa-onnx-1.13.5"
            }))));
            Directory.CreateDirectory(AlifePath.TempFolderPath);
            string output = Path.Combine(AlifePath.TempFolderPath, $"vits_onnx_{key}.wav");
            if (File.Exists(output) && new FileInfo(output).Length > 44) return output;
            string temporary = output + $".{Guid.NewGuid():N}.wav";
            try
            {
                await Task.Run(() => {
                    ct.ThrowIfCancellationRequested();
                    InitializeModel(settings, model);
                    ct.ThrowIfCancellationRequested();
                    if (speaker >= synthesizer!.NumSpeakers)
                        throw new ArgumentOutOfRangeException(nameof(Configuration.SpeakerId), $"说话人 ID 应为 0–{synthesizer.NumSpeakers - 1}。");
                    var audio = synthesizer.GenerateWithCallback(normalized, 1, speaker, (_, _) => ct.IsCancellationRequested ? 0 : 1);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        if (audio.NumSamples <= 0 || audio.SampleRate <= 0 || !audio.SaveToWaveFile(temporary))
                            throw new IOException("VITS 未生成有效音频。");
                    }
                    finally { audio.Dispose(); }
                }, CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, output, overwrite: true);
                return output;
            }
            finally { File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    EngineSettings GetSettings()
    {
        var settings = new EngineSettings(GetModelDirectory(Configuration), Configuration.NoiseScale,
            Configuration.NoiseScaleW, Configuration.LengthScale, Configuration.WorkerThreads);
        if (!float.IsFinite(settings.NoiseScale) || settings.NoiseScale < 0 ||
            !float.IsFinite(settings.NoiseScaleW) || settings.NoiseScaleW < 0 ||
            !float.IsFinite(settings.LengthScale) || settings.LengthScale <= 0 ||
            settings.WorkerThreads < 1 || settings.WorkerThreads > 4 || Configuration.SpeakerId < 0)
            throw new ArgumentException("VITS 合成参数无效。线程数应为 1–4，长度规模必须大于 0，噪声参数与说话人 ID 不能小于 0。");
        return settings;
    }

    void InitializeModel(EngineSettings settings, FileInfo model)
    {
        if (synthesizer == null || settings != loadedSettings || loadedModelTime != model.LastWriteTimeUtc)
        {
            synthesizer?.Dispose();
            synthesizer = null;
            var config = new OfflineTtsConfig();
            config.Model.Vits.Model = model.FullName;
            config.Model.Vits.Tokens = Path.Combine(settings.Directory, "tokens.txt");
            config.Model.Vits.Lexicon = Path.Combine(settings.Directory, "lexicon.txt");
            config.Model.Vits.NoiseScale = settings.NoiseScale;
            config.Model.Vits.NoiseScaleW = settings.NoiseScaleW;
            config.Model.Vits.LengthScale = settings.LengthScale;
            config.Model.NumThreads = settings.WorkerThreads;
            config.Model.Provider = "cpu";
            config.Model.Debug = 0;
            logger.LogInformation("正在加载 VITS ONNX 模型，推理线程数：{Threads}", settings.WorkerThreads);
            synthesizer = new OfflineTts(config);
            loadedSettings = settings;
            loadedModelTime = model.LastWriteTimeUtc;
        }
    }

    async Task EnsureModelAsync(CancellationToken cancellationToken)
    {
        if (!IsModelReady(Configuration) && string.IsNullOrWhiteSpace(Configuration.ModelDirectory))
            await VitsModelDownload.EnsureAsync(GetModelDirectory(Configuration), logger, cancellationToken);
        if (!IsModelReady(Configuration))
            throw new FileNotFoundException($"模型目录不完整：{GetModelDirectory(Configuration)}。需要 alife-paimon.onnx、tokens.txt、lexicon.txt；清空模型目录配置可自动下载官方模型。");
    }

    static string NormalizeText(string text)
    {
        string normalized = text.Replace('，', ',').Replace('。', '.').Replace('！', '!').Replace('？', '?')
            .Replace('；', ',').Replace('：', ',').Replace('、', ',').Replace('—', '-');
        const string digits = "零一二三四五六七八九";
        StringBuilder result = new(normalized.Length * 2);
        foreach (char c in normalized)
        {
            result.Append(c >= '0' && c <= '9' ? digits[c - '0'] : c);
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) result.Append(' ');
        }
        return result.ToString();
    }
}
