namespace Alife.Function.Speech.VITS;

public class VitsSpeechModelConfig
{
    public string ModelDirectory { get; set; } = "";
    public int WorkerThreads { get; set; } = 2;
    public int SpeakerId { get; set; } = 551;
    public float NoiseScale { get; set; } = 0.6f;
    public float NoiseScaleW { get; set; } = 0.668f;
    public float LengthScale { get; set; } = 1.2f;
}
