namespace Alife.Function.Speech;

/// <summary>
/// VITS-based speech synthesizer with configurable parameters.
/// Stub implementation - full functionality not included in this repository version.
/// </summary>
public class VitsSpeechSynthesizer : SpeechSynthesizer
{
    public int SpeakerId { get; set; }
    public float NoiseScale { get; set; }
    public float NoiseScaleW { get; set; }
    public float LengthScale { get; set; }

    public VitsSpeechSynthesizer(float noiseScale = 0.6f, float noiseScaleW = 0.668f, float lengthScale = 1.2f, int speakerId = 551)
    {
        NoiseScale = noiseScale;
        NoiseScaleW = noiseScaleW;
        LengthScale = lengthScale;
        SpeakerId = speakerId;
    }

    public override Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("VitsSpeechSynthesizer is not fully implemented in this version.");
    }
}
