using System;

namespace Alife.Function.AIModelUtility;

public interface IAuditoryModel
{
    event Action<string>? Recognized;
    void AcceptWaveform(float[] samples);
}
