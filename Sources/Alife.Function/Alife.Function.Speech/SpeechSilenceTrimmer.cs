#if !ANDROID
using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace Alife.Function.Speech;

public class SpeechSilenceTrimmer : ISampleProvider
{
    public WaveFormat WaveFormat { get; }
    readonly float[] samples;
    int position;

    public SpeechSilenceTrimmer(ISampleProvider source, float threshold = 0.01f)
    {
        WaveFormat = source.WaveFormat;
        var allSamples = new List<float>();
        float[] tempBuffer = new float[WaveFormat.SampleRate];
        int read;
        while ((read = source.Read(tempBuffer, 0, tempBuffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                allSamples.Add(tempBuffer[i]);
        }

        int start = 0;
        while (start < allSamples.Count && Math.Abs(allSamples[start]) <= threshold)
            start++;

        int end = allSamples.Count - 1;
        while (end > start && Math.Abs(allSamples[end]) <= threshold)
            end--;

        if (start <= end)
        {
            int length = end - start + 1;
            samples = new float[length];
            allSamples.CopyTo(start, samples, 0, length);
        }
        else
        {
            samples = Array.Empty<float>();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int available = samples.Length - position;
        int toCopy = Math.Min(available, count);
        if (toCopy > 0)
        {
            samples.AsSpan(position, toCopy).CopyTo(buffer.AsSpan(offset, toCopy));
            position += toCopy;
        }
        return toCopy;
    }

    /// <summary>按 windowMs 分帧计算 RMS 音量包络（含增益，范围 0..1），供口型同步按时钟查表。</summary>
    public float[] GetEnvelope(int windowMs)
    {
        if (samples.Length == 0)
            return Array.Empty<float>();
        int step = Math.Max(1, WaveFormat.SampleRate * WaveFormat.Channels * windowMs / 1000);
        int count = (samples.Length + step - 1) / step;
        float[] result = new float[count];
        for (int i = 0; i < count; i++)
        {
            int start = i * step;
            int end = Math.Min(samples.Length, start + step);
            double sum = 0;
            for (int j = start; j < end; j++)
                sum += (double)samples[j] * samples[j];
            double rms = end > start ? Math.Sqrt(sum / (end - start)) : 0;
            result[i] = (float)Math.Min(1.0, Math.Sqrt(rms) * 2.2);
        }
        return result;
    }
}
#endif
