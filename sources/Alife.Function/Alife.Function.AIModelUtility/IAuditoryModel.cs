using System;

namespace Alife.Function.AIModelUtility;

public interface IAuditoryModel
{
    event Action<string>? Recognized;
    /// <summary>喂入 16kHz 单声道 float 波形，只处理前 length 个采样（数组可复用）</summary>
    void AcceptWaveform(float[] samples, int length);
}

public interface IAudioRecognizer : IAuditoryModel, IDisposable
{
    /// <summary>结束当前会话，促使 VAD 将末尾语音段输出（同步，返回后识别结果均已产出）</summary>
    void Flush();
}

public interface IAudioRecognizerProvider
{
    /// <summary>创建一个新的流式识别器（独立 VAD）</summary>
    IAudioRecognizer CreateAudioRecognizer();
}