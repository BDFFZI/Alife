using System;
using System.Collections.Generic;
using System.IO;
using Alife.Foundation;
using Alife.Function.Auditory;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 音频匹配检测器：加载参考音频 PCM，用滚动缓冲区存储实时音频，
/// 每次比对完整参考长度。相关系数超过阈值时标记命中。
/// </summary>
public sealed class AudioMatchDetector : IDisposable
{
    readonly AudioService audioService;
    readonly float[] referencePcm;
    readonly float threshold;
    readonly object gate = new();
    readonly HashSet<string> patterns = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> heardPatterns = new(StringComparer.OrdinalIgnoreCase);

    readonly float[] buffer;
    int bufferPos;
    int bufferFill;
    int logCount;

    /// <summary>最近一次比对的相关系数（0~1）。</summary>
    public float LastSimilarity { get; private set; }

    public AudioMatchDetector(AudioService audioService, string audioFilePath, double threshold = 0.8, int sampleCount = 1024)
    {
        this.audioService = audioService;
        this.threshold = (float)threshold;

        referencePcm = LoadReference(audioFilePath);
        buffer = new float[referencePcm.Length];
    }

    public void AddPattern(string id)
    {
        lock (gate) { patterns.Add(id); }
        audioService.AddListener(OnWaveformReady);
    }

    public void RemovePattern(string id)
    {
        lock (gate)
        {
            patterns.Remove(id);
            heardPatterns.Remove(id);
            if (patterns.Count == 0)
                audioService.RemoveListener(OnWaveformReady);
        }
    }

    public string Consume(string id)
    {
        lock (gate)
        {
            return !string.IsNullOrEmpty(id) && heardPatterns.Remove(id) ? "true" : "false";
        }
    }

    void OnWaveformReady(float[] samples, int count)
    {
        // 写入滚动缓冲区
        for (int i = 0; i < count; i++)
        {
            buffer[bufferPos] = samples[i];
            bufferPos = (bufferPos + 1) % buffer.Length;
            if (bufferFill < buffer.Length) bufferFill++;
        }

        // 缓冲区未满时不比对
        if (bufferFill < buffer.Length) return;

        // 归一化互相关：比较整个缓冲区和参考音频
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            // 从 bufferPos 开始读取（最旧的数据在前）
            int idx = (bufferPos + i) % buffer.Length;
            dot += referencePcm[i] * buffer[idx];
            normA += referencePcm[i] * referencePcm[i];
            normB += buffer[idx] * buffer[idx];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        float similarity = denom < 1e-9f ? 0 : MathF.Abs(dot / denom);
        LastSimilarity = similarity;

        if (logCount++ % 50 == 0)
            AlifeLog.LogInformation($"[Companion] AudioMatch: 缓冲={bufferFill}/{buffer.Length}, 相似度={similarity:F4}");

        if (similarity >= threshold)
        {
            lock (gate)
            {
                foreach (string id in patterns)
                    heardPatterns.Add(id);
            }
        }
    }

    static float[] LoadReference(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("参考音频文件不存在", filePath);

        float[] audio = AudioDecoder.DecodeFileTo16kMonoFloat(filePath);
        if (audio.Length == 0)
            throw new InvalidOperationException("参考音频文件解码后为空");

        // 跳过前导静音
        int start = 0;
        while (start < audio.Length && MathF.Abs(audio[start]) < 0.001f)
            start++;

        if (start > 0)
        {
            float[] trimmed = new float[audio.Length - start];
            Array.Copy(audio, start, trimmed, 0, trimmed.Length);
            audio = trimmed;
        }

        AlifeLog.LogInformation($"[Companion] AudioMatch: 加载参考音频 {filePath}, 有效长度={audio.Length} 样本");
        return audio;
    }

    public void Dispose()
    {
        audioService.RemoveListener(OnWaveformReady);
    }
}
