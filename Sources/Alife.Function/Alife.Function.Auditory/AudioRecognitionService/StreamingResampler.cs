using System;
using NAudio.Wave;

namespace Alife.Function.Auditory;

/// <summary>
/// 流式采样率转换器：将任意 IEEE float 交错格式（如 48kHz 双声道）实时转换为 16kHz 单声道 float。
/// 相位累加 + 线性插值，跨块保留上一帧（<see cref="carryFrame"/>）保证块边界无缝衔接。
/// 输入多少帧就产出多少对应输出（输出时长 = 输入时长 × 16000/源采样率），绝不补零，
/// 因此不会像 <see cref="MediaFoundationResampler"/> 那样在输入不足时用全零填充、切断语音段。
/// </summary>
public class StreamingResampler
{
    public StreamingResampler(WaveFormat inputFormat)
    {
        ratio = inputFormat.SampleRate / 16000.0;
        channels = inputFormat.Channels;
    }

    /// <summary>
    /// 转换一块交错 float 音频为 16k 单声道 float。
    /// </summary>
    /// <param name="input">交错 float 输入（源格式）</param>
    /// <param name="count">输入采样数（含所有声道）</param>
    /// <param name="output">输出缓冲，长度需 ≥ count（真实源采样率 ≥ 16k，输出帧数 ≤ 输入帧数）</param>
    /// <returns>产出的输出采样数</returns>
    public int Process(float[] input, int count, float[] output)
    {
        int inFrames = count / channels;
        int outCount = 0;

        while (outCount < output.Length)
        {
            int p0 = (int)Math.Floor(phase);
            if (p0 + 1 >= inFrames)
                break; //需要的第 p0+1 帧在下一块，保持 phase 等待下一块，避免补零

            float f0 = MonoAt(input, p0);
            float f1 = MonoAt(input, p0 + 1);
            double frac = phase - p0;
            output[outCount++] = (float)(f0 + (f1 - f0) * frac);
            phase += ratio;
        }

        if (inFrames > 0)
        {
            //保存本块最后一帧作为下一块的插值边界
            if (carryFrame == null)
                carryFrame = new float[channels];
            Array.Copy(input, (inFrames - 1) * channels, carryFrame, 0, channels);
            hasCarry = true;
            //位置基准移到本块末尾：phase 变为相对下一块的偏移（可为负，指向 carryFrame）
            phase -= inFrames;
        }
        return outCount;
    }

    readonly double ratio;
    readonly int channels;
    double phase;
    float[]? carryFrame;
    bool hasCarry;

    float MonoAt(float[] input, int frameIndex)
    {
        if (frameIndex == -1)
        {
            if (hasCarry == false)
                return 0f;
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += carryFrame![c];
            return sum / channels;
        }

        int baseIndex = frameIndex * channels;
        float s = 0;
        for (int c = 0; c < channels; c++)
            s += input[baseIndex + c];
        return s / channels;
    }
}
