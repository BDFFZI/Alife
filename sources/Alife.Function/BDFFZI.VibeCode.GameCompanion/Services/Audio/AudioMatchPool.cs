namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 音频匹配检测器池：引用计数，多个采样器共享同一个检测器和音频服务。
/// </summary>
public static class AudioMatchPool
{
    static AudioMatchDetector? shared;
    static AudioService? audioService;
    static int refCount;
    static readonly object gate = new();

    /// <summary>获取共享检测器（引用计数 +1）。</summary>
    public static AudioMatchDetector Acquire(string audioFilePath, double threshold, int sampleCount)
    {
        lock (gate)
        {
            if (shared == null)
            {
                audioService = AudioService.Acquire();
                shared = new AudioMatchDetector(audioService, audioFilePath, threshold, sampleCount);
            }
            refCount++;
            return shared;
        }
    }

    /// <summary>释放引用（引用计数 -1）。归零时销毁检测器和音频服务。</summary>
    public static void Release()
    {
        lock (gate)
        {
            refCount--;
            if (refCount <= 0 && shared != null)
            {
                shared.Dispose();
                shared = null;
                audioService?.Release();
                audioService = null;
                refCount = 0;
            }
        }
    }
}
