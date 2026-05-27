namespace Alife.Function.Speech;

/// <summary>
/// Edge-TTS based speech synthesizer.
/// Stub implementation - full functionality not included in this repository version.
/// </summary>
public class EdgeSpeechSynthesizer : SpeechSynthesizer
{
    public string VoiceTone { get; set; }

    public EdgeSpeechSynthesizer(string voiceTone)
    {
        VoiceTone = voiceTone;
    }

    public override Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("EdgeSpeechSynthesizer is not fully implemented in this version.");
    }
}
