using System;

namespace Alife.Function.Speech;

/// <summary>
/// 语音播放时的真音频上下文：按时间分帧的音量包络 + 播放对齐信息。
/// 做成 record 而不是裸数组，方便未来扩展（如总时长、采样率、文本）。
/// </summary>
public class SpeechAudioContext
{
    /// <summary>每帧音量包络，范围 0..1。</summary>
    public required float[] Envelope { get; set; }
    /// <summary>单帧时长（毫秒），前端用 elapsed / WindowMs 算下标。</summary>
    public int WindowMs { get; set; }
    /// <summary>播放起点（毫秒 Unix 时间戳），前端用 Date.now() - StartUnixMs 对表。</summary>
    public long StartUnixMs { get; set; }
};

/// <summary>
/// 语音服务抽象：桌宠等模块通过它订阅真音频播放事件，实现无文件落盘的口型同步。
/// 由语音插件实现；未安装语音插件时注入为 null，调用方回落到原有行为。
/// 注意：依赖此接口的模块在语音插件重载后也必须重载（见框架指南）。
/// </summary>
public interface ISpeechService
{
    bool IsSpeaking { get; }

    /// <summary>音频真正开始播放后触发，携带整段包络供前端按时钟查表。</summary>
    event Action<SpeechAudioContext>? AudioStarted;

    /// <summary>播放结束或被打断时触发，前端据此闭嘴。</summary>
    event Action? AudioStopped;
}