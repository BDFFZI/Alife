using Alife.Function.AIModelUtility;

namespace Alife.Function.GameCompanion.Audio;

/// <summary>
/// 语音识别器共享池：多个语音采样器复用同一识别器与录音器；
/// 引用计数归零时回收（停止监听并释放）。由语音采样器内部使用，框架不感知。
/// </summary>
public static class VoiceDetectorPool
{
    static readonly object gate = new();
    static IAudioRecognizerProvider? provider;
    static VoiceKeywordDetector? shared;
    static int references;

    /// <summary>注入语音识别提供者（模块启动时调用；未提供则语音不可用）。</summary>
    public static void Initialize(IAudioRecognizerProvider? p)
    {
        lock (gate)
        {
            provider = p;
        }
    }

    /// <summary>获取共享识别器（引用计数 +1）。</summary>
    public static VoiceKeywordDetector Acquire()
    {
        lock (gate)
        {
            references++;
            if (shared is null)
                shared = new VoiceKeywordDetector(provider);
            return shared;
        }
    }

    /// <summary>归还共享识别器（引用计数 -1，归零时回收）。</summary>
    public static void Release()
    {
        lock (gate)
        {
            references--;
            if (references <= 0)
            {
                shared?.Dispose();
                shared = null;
                references = 0;
            }
        }
    }
}