using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Framework;

public record struct TokenUsage
{
    public int Total { get; set; }
    public int Input { get; set; }
    public int Cached { get; set; }
    public int Output { get; set; }

    public static TokenUsage operator +(TokenUsage a, TokenUsage b)
    {
        return new TokenUsage {
            Total = a.Total + b.Total,
            Input = a.Input + b.Input,
            Cached = a.Cached + b.Cached,
            Output = a.Output + b.Output,
        };
    }
}

public interface ILanguageModel
{
    public Task<string> ChatStreamingAsync(
        ChatHistoryAgentThread chatHistoryAgentThread,
        Action<string>? textReceived = null,
        Action<string>? thinkReceived = null,
        Action<TokenUsage>? tokenUsed = null,
        Action<Exception>? exceptionThrow = null,
        CancellationToken cancellationToken = default
    );
}
