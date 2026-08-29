using System;
using System.Numerics;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 极简 FFT 工具：计算幅度谱，供音频特征提取使用。
/// </summary>
public static class FftHelper
{
    /// <summary>
    /// 计算信号的幅度谱（归一化到 0~1）。
    /// 输入为 16kHz mono float 采样点，输出为前 N/2 个频率桶的幅度。
    /// </summary>
    public static float[] ComputeMagnitudeSpectrum(float[] samples, int count)
    {
        int n = CountPow2(count);
        var complex = new Complex[n];
        for (int i = 0; i < n; i++)
            complex[i] = i < count ? new Complex(samples[i], 0) : Complex.Zero;

        FFT(complex);

        int bins = n / 2;
        var spectrum = new float[bins];
        float max = 0;
        for (int i = 0; i < bins; i++)
        {
            float mag = (float)complex[i].Magnitude;
            spectrum[i] = mag;
            if (mag > max) max = mag;
        }

        // 归一化
        if (max > 1e-9f)
            for (int i = 0; i < bins; i++)
                spectrum[i] /= max;

        return spectrum;
    }

    /// <summary>计算两个幅度谱的余弦相似度（-1~1，越大越相似）。</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-9f ? 0 : dot / denom;
    }

    static int CountPow2(int count)
    {
        int n = 1;
        while (n < count) n <<= 1;
        return n;
    }

    /// <summary>Cooley-Tukey radix-2 FFT（原位）。</summary>
    static void FFT(Complex[] x)
    {
        int n = x.Length;
        if (n == 0) return;

        // 位反转置换
        int bits = 0;
        while ((1 << bits) < n) bits++;
        for (int i = 0; i < n; i++)
        {
            int j = BitReverse(i, bits);
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }

        // 蝶形运算
        for (int size = 2; size <= n; size *= 2)
        {
            int half = size / 2;
            double angle = -2.0 * Math.PI / size;
            var wn = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < n; i += size)
            {
                var w = Complex.One;
                for (int j = 0; j < half; j++)
                {
                    var u = x[i + j];
                    var t = w * x[i + j + half];
                    x[i + j] = u + t;
                    x[i + j + half] = u - t;
                    w *= wn;
                }
            }
        }
    }

    static int BitReverse(int x, int bits)
    {
        int result = 0;
        for (int i = 0; i < bits; i++)
        {
            result = (result << 1) | (x & 1);
            x >>= 1;
        }
        return result;
    }
}
