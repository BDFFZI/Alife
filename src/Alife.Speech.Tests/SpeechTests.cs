using Alife.Speech;
using Xunit;

namespace Alife.Speech.Tests;

public class SpeechTests
{
    [Fact]
    public void TestSpeechSynthesizer()
    {
        var synth = new LocalSpeechSynthesizer();
        Assert.NotNull(synth);
    }
}
