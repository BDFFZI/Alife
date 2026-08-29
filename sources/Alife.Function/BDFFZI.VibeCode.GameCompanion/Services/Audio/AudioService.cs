using System;
using System.Collections.Generic;
using Alife.Foundation;
using Alife.Function.Auditory;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 统一音频服务：拥有唯一一个 <see cref="SystemAudioRecorder"/>，所有音频类采样器共享。
/// 通过引用计数管理录音器生命周期。
/// </summary>
public sealed class AudioService : IDisposable
{
    static AudioService? instance;
    static int refCount;
    static readonly object gate = new();

    readonly SystemAudioRecorder recorder;
    readonly List<Action<float[], int>> listeners = new();
    bool disposed;

    AudioService()
    {
        recorder = new SystemAudioRecorder();
        recorder.WaveformReady += OnWaveformReady;
    }

    /// <summary>获取共享音频服务（引用计数 +1）。</summary>
    public static AudioService Acquire()
    {
        lock (gate)
        {
            instance ??= new AudioService();
            refCount++;
            return instance;
        }
    }

    /// <summary>释放引用（引用计数 -1）。归零时停止录音。</summary>
    public void Release()
    {
        lock (gate)
        {
            refCount--;
            if (refCount <= 0)
            {
                Stop();
                refCount = 0;
            }
        }
    }

    /// <summary>订阅音频数据。首次订阅时启动录音。</summary>
    public void AddListener(Action<float[], int> callback)
    {
        lock (gate)
        {
            listeners.Add(callback);
            if (listeners.Count == 1)
                Start();
        }
    }

    /// <summary>取消订阅。无订阅者时停止录音。</summary>
    public void RemoveListener(Action<float[], int> callback)
    {
        lock (gate)
        {
            listeners.Remove(callback);
            if (listeners.Count == 0)
                Stop();
        }
    }

    void Start()
    {
        try
        {
            AlifeLog.LogInformation("[Companion] AudioService.Start: 启动系统声音监听");
            recorder.Start("system");
            AlifeLog.LogInformation("[Companion] AudioService.Start: 录音器已启动");
        }
        catch (Exception ex)
        {
            AlifeLog.LogError($"[Companion] 系统声音监听启动失败: {ex.Message}");
        }
    }

    void Stop()
    {
        try { recorder.Stop(); }
        catch { }
    }

    void OnWaveformReady(float[] samples, int count)
    {
        Action<float[], int>[] snapshot;
        lock (gate)
        {
            if (listeners.Count == 0) return;
            snapshot = listeners.ToArray();
        }
        foreach (var listener in snapshot)
        {
            try { listener(samples, count); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        recorder?.Dispose();
    }
}
