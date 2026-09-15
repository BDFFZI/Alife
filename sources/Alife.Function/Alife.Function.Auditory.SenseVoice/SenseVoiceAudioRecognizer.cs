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

                //先记录本块原始音频，供输出语音段时补上被 VAD 切掉的开头（与 VAD 坐标同步）
                WriteHistory(part, count);

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
    //语音段开头补音采样数：取 VAD 的最小静音时长（min_silence_duration）。
    //VAD 的起音检测约晚于真实起音 30~60ms（软起音如"嗯""那个"更晚），导致语音段
    //起点落在第一个字之后、解码丢字，因此给每个语音段补回起点之前的这段原始音频。
    //补音长度等于 min_silence_duration：两段语音间静音至少这么久，补音基本不会
    //跨进上一段（最坏越界 ~34ms），且与用户配置自动联动。
    readonly int preRollSamples = (int)(vadConfig.SileroVad.MinSilenceDuration * vadConfig.SampleRate);
    //历史缓冲：保存原始音频，供输出语音段时补开头。
    //8s 足以覆盖最长语音段 + 前置补音。
    const int HistoryCapacity = 16000 * 8;
    readonly VoiceActivityDetector vad = new(vadConfig, bufferSizeInSeconds: 30);
    readonly Lock syncLock = new(); //防止多线程并发喂 VAD（实时麦克风路径会多线程投递）
    readonly float[] chunkBuffer = new float[ChunkSize];
    readonly float[] silenceBuffer = new float[16000];
    readonly float[] history = new float[HistoryCapacity];
    long absolutePosition; //已写入 history 的采样总数（与 VAD 的坐标一致）

    void WriteHistory(float[] samples, int length)
    {
        int capacity = history.Length;
        int pos = (int)(absolutePosition % capacity);
        int offset = 0;
        while (length > 0)
        {
            int chunk = Math.Min(length, capacity - pos);
            Array.Copy(samples, offset, history, pos, chunk);
            offset += chunk;
            length -= chunk;
            pos += chunk;
            if (pos == capacity)
                pos = 0;
        }
        absolutePosition += offset;
    }

    void Drain()
    {
        while (vad.IsEmpty() == false)
        {
            SpeechSegment segment = vad.Front();
            if (segment.Samples is { Length: > 0 })
            {
                string text = DecodeSegment(PrependPreRoll(segment));
                if (string.IsNullOrWhiteSpace(text) == false && text != "。")
                    Recognized?.Invoke(text);
            }
            vad.Pop();
        }
    }

    /// <summary>在语音段开头补上 VAD 未包含的前置音频（紧邻语音段起点的历史采样）</summary>
    float[] PrependPreRoll(SpeechSegment segment)
    {
        int preStart = (int)Math.Max(0, (long)segment.Start - preRollSamples);
        int preCount = segment.Start - preStart;
        if (preCount <= 0)
            return segment.Samples;

        float[] result = new float[preCount + segment.Samples.Length];
        for (int i = 0; i < preCount; i++)
            result[i] = history[(int)(((long)preStart + i) % history.Length)];
        Array.Copy(segment.Samples, 0, result, preCount, segment.Samples.Length);
        return result;
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