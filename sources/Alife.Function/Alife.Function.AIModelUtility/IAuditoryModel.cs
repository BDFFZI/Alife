using System;

namespace Alife.Function.Auditory;

public interface IAuditoryModel
{
    event Action<string>? Recognized;
    void AcceptWaveform(float[] samples);
}
