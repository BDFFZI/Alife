using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Speech;

public interface ISpeechModel
{
    Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default);
}
