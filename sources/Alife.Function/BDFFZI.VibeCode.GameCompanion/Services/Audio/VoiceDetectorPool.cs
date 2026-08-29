using System;
using Alife.Function.AIModelUtility;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 语音关键词检测器池：引用计数，多个采样器共享同一个检测器和音频服务。
/// </summary>
public static class VoiceDetectorPool
{
    static VoiceKeywordDetector? shared;
    static AudioService? audioService;
    static int refCount;
    static readonly object gate = new();
    static IAudioRecognizerProvider? provider;

    /// <summary>初始化语音识别提供者（由模块 OnAwake 调用）。</summary>
    public static void Initialize(IAudioRecognizerProvider? recognizerProvider)
    {
        provider = recognizerProvider;
    }

    /// <summary>获取共享检测器（引用计数 +1）。</summary>
    public static VoiceKeywordDetector Acquire()
    {
        lock (gate)
        {
            if (shared == null)
            {
                audioService = AudioService.Acquire();
                shared = new VoiceKeywordDetector(audioService, provider);
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
