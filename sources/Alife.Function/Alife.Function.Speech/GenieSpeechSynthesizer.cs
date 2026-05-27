namespace Alife.Function.Speech;

/// <summary>
/// Genie-based speech synthesizer.
/// Stub implementation - full functionality not included in this repository version.
/// </summary>
public class GenieSpeechSynthesizer : SpeechSynthesizer
{
    public override Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("GenieSpeechSynthesizer is not fully implemented in this version.");
    }
}
