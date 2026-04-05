using NAudio.Wave;
using SherpaOnnx;

namespace Alife.Speech;

public class SpeechRecognizer : IDisposable
{
    public event Action<string>? Recognized;
    public bool IsRecognizing => waveIn != null;
    public void Start()
    {
        if (IsRecognizing)
            throw new InvalidOperationException("已在运行中，Stop 后才能再次 Start。");

        waveIn = new WaveInEvent();
        waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
        waveIn.DataAvailable += (_, e) => AcceptWaveform(e.Buffer, e.BytesRecorded);
        waveIn.StartRecording();
    }
    public void Stop()
    {
        if (waveIn == null)
            throw new InvalidOperationException("未在运行中，Start 后才可调用 Stop。");

        waveIn.StopRecording();
        waveIn.Dispose();
        waveIn = null!;
    }

    readonly OfflineRecognizer recognizer;
    readonly VoiceActivityDetector vad;
    WaveInEvent? waveIn;

    public SpeechRecognizer(string modelRootPath)
    {
        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(modelRootPath, "sensevoice-small", "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = "zh";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.Tokens = Path.Combine(modelRootPath, "sensevoice-small", "tokens.txt");
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;

        recognizer = new OfflineRecognizer(config);

        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = Path.Combine(modelRootPath, "silero-vad", "silero_vad.onnx");
        vadConfig.SileroVad.Threshold = 0.5f;
        vadConfig.SileroVad.MinSilenceDuration = 0.5f;
        vadConfig.SileroVad.MinSpeechDuration = 0.25f;
        vadConfig.SampleRate = 16000;

        vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 60);
    }

    public void Dispose()
    {
        if (IsRecognizing)
            Stop();

        recognizer.Dispose();
        vad.Dispose();
        GC.SuppressFinalize(this);
    }

    void AcceptWaveform(byte[] buffer, int bytesRecorded)
    {
        float[] samples = new float[bytesRecorded / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(buffer, i * 2) / 32768.0f;

        vad.AcceptWaveform(samples);
        while (vad.IsEmpty() == false)
        {
            SpeechSegment segment = vad.Front();
            if (segment.Samples is { Length: > 0 })
                ProcessSegment(segment.Samples);
            vad.Pop();
        }
    }

    void ProcessSegment(float[] samples)
    {
        using OfflineStream stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        recognizer.Decode(stream);

        if (string.IsNullOrWhiteSpace(stream.Result.Text) == false)
            Recognized?.Invoke(stream.Result.Text);
    }
}
