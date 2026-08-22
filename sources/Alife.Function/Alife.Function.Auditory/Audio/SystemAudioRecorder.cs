using System;
using NAudio.Wave;

namespace Alife.Function.Auditory;

/// <summary>
/// 可复用的系统/麦克风录音器。
/// 将 NAudio 的采样器（WasapiLoopbackCapture 系统扬声器 / WaveInEvent 麦克风）对象化为独立组件：
/// 通过 <see cref="Start"/>/<see cref="Stop"/> 管理生命周期，将任意格式的音频流统一转换为
/// 16kHz 单声道 IEEE float 后以 <see cref="WaveformReady"/> 事件对外输出。
/// 供本插件（AudioRecognitionService）及外部插件（如游戏陪玩）复用。
/// </summary>
public class SystemAudioRecorder : IDisposable
{
    /// <summary>一帧 16kHz 单声道 float 波形。缓冲会被复用，仅处理前 length 个采样。</summary>
    public event Action<float[], int>? WaveformReady;

    /// <summary>是否正在录音。</summary>
    public bool IsRecording { get; private set; }

    /// <summary>当前声源：system（扬声器回环）或 mic（麦克风）。</summary>
    public string Source { get; private set; } = "";

    /// <summary>
    /// 开始录音。
    /// </summary>
    /// <param name="source">system：监听扬声器输出（游戏/视频声音）；mic：监听麦克风。</param>
    public void Start(string source = "system")
    {
        if (IsRecording)
            throw new InvalidOperationException("录音器已在运行，请先调用 Stop()");

        lock (syncRoot)
        {
            if (IsRecording)
                return;

            Source = source;
            //唯一分支：创建采样器（麦克风或系统声）
            sampler = source == "mic"
                ? new WaveInEvent { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1) }
                : new WasapiLoopbackCapture();

            //system 声是设备 MixFormat（如 48k 双声道 float），需要流式转成 16k mono float；
            //mic 已是 16k mono float，无需转换。转换在 OnDataAvailable 中直接完成。
            resampler = source == "mic" ? null : new StreamingResampler(sampler.WaveFormat);

            sampler.DataAvailable += OnDataAvailable;
            sampler.StartRecording();
            IsRecording = true;
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            if (!IsRecording)
                return;

            if (sampler != null)
            {
                sampler.DataAvailable -= OnDataAvailable;
                sampler.StopRecording();
                sampler.Dispose();
                sampler = null;
            }
            resampler = null;
            convInput = null;
            convOutput = null;
            IsRecording = false;
        }
    }

    void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;

        lock (syncRoot)
        {
            if (sampler == null)
                return;

            //字节 → float（复用缓冲，避免分配）
            int sampleCount = e.BytesRecorded / 4;
            if (convInput == null || convInput.Length < sampleCount)
                convInput = new float[sampleCount];
            Buffer.BlockCopy(e.Buffer, 0, convInput, 0, e.BytesRecorded);

            if (resampler != null)
            {
                if (convOutput == null || convOutput.Length < sampleCount)
                    convOutput = new float[sampleCount];
                int converted = resampler.Process(convInput, sampleCount, convOutput);
                if (converted > 0)
                    WaveformReady?.Invoke(convOutput, converted);
            }
            else
            {
                WaveformReady?.Invoke(convInput, sampleCount);
            }
        }
    }

    public void Dispose() => Stop();

    readonly object syncRoot = new();
    IWaveIn? sampler;
    StreamingResampler? resampler;
    float[]? convInput;
    float[]? convOutput;
}
