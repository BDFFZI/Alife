using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Speech;

/// <summary>
/// 平台音频播放器：由客户端或插件自身提供实现，用于播放合成语音。
/// 未提供时语音插件回落到内置的 NAudio 实现。
/// </summary>
public interface ISpeechAudioPlayer
{
    /// <summary>播放音频文件，返回时代表播放完成（或失败抛出异常）。</summary>
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>停止当前播放（打断），PlayAsync 将立即返回。</summary>
    void Stop();
}
