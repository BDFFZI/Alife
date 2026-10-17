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
}
