using System;
using System.Threading;
using SherpaOnnx;
using Alife.Function.AIModelUtility;

namespace Alife.Function.Auditory.SenseVoice;

/// <summary>
/// 基于 SenseVoice 的流式语音识别器：拥有独立 VAD 实例（不污染实时识别状态），
/// 共享模型的识别器（解码时加锁串行）。多次喂入保持 VAD 连续性，实现真流式。
/// <see cref="AcceptWaveform"/> 同步处理：喂入后立即切割并解码，识别到的语音段同步触发 <see cref="Recognized"/>。
/// 内部使用复用缓冲，配合 (samples, length) 入参实现零分配。
/// </summary>
public class SenseVoiceAudioRecognizer(OfflineRecognizer recognizer, VadModelConfig vadConfig) : IAudioRecognizer
{
    public event Action<string>? Recognized;

    public void AcceptWaveform(float[] samples, int length)
    {
        length = Math.Min(length, samples.Length);
        if (length <= 0)
            return;

        lock (syncLock)
        {
            //按 100ms 分块喂入（VAD 需小步长才能正确切割语音段）
            for (int offset = 0; offset < length; offset += ChunkSize)
            {
                int count = Math.Min(ChunkSize, length - offset);
                //完整块复用 chunkBuffer；末尾小块长度不定，复制到一次性数组
                float[] part = count == ChunkSize ? chunkBuffer : new float[count];
                Array.Copy(samples, offset, part, 0, count);

                vad.AcceptWaveform(part);
                Drain();
            }
        }
    }
    public void Flush()
    {
        //喂入 1 秒静音，促使 VAD 输出末尾语音段（复用静音缓冲）
        AcceptWaveform(silenceBuffer, silenceBuffer.Length);
    }
    public void Dispose()
    {
        vad.Dispose();
    }

    const int ChunkSize = 1600; //16000Hz * 0.1s
    readonly VoiceActivityDetector vad = new(vadConfig, bufferSizeInSeconds: 30);
    readonly Lock syncLock = new(); //防止多线程并发喂 VAD（实时麦克风路径会多线程投递）
    readonly float[] chunkBuffer = new float[ChunkSize];
    readonly float[] silenceBuffer = new float[16000];

    void Drain()
    {
        while (vad.IsEmpty() == false)
        {
            SpeechSegment segment = vad.Front();
            if (segment.Samples is { Length: > 0 })
            {
                string text = DecodeSegment(segment.Samples);
                if (string.IsNullOrWhiteSpace(text) == false && text != "。")
                    Recognized?.Invoke(text);
            }
            vad.Pop();
        }
    }
    string DecodeSegment(float[] samples)
    {
        //与实时识别共用解码器，加锁串行保证线程安全
        lock (recognizer)
        {
            using OfflineStream stream = recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            recognizer.Decode(stream);
            return stream.Result.Text;
        }
    }
}
