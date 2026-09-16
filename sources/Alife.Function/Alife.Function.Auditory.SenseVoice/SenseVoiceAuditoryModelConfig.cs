namespace Alife.Function.Auditory.SenseVoice;

public class SenseVoiceAuditoryModelConfig
{
    public string Language { get; set; } = "zh";
    public bool UseInverseTextNormalization { get; set; } = true;
    public int NumThreads { get; set; } = 1;

    public float VadThreshold { get; set; } = 0.2f;
    public float VadMinSilenceDuration { get; set; } = 0.5f;
    public float VadMinSpeechDuration { get; set; } = 0.25f;
}
