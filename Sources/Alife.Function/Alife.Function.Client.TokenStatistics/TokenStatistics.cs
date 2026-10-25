using System;

namespace Alife.Function.Client.TokenStatistics;

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

public struct TokenUsageContext
{
    public string UserMessage { get; init; }
    public string AIThinking { get; init; }
    public string AIMessage { get; init; }
    public TokenUsage TokenUsage { get; init; }
}

public interface ITokenUser
{
    public event Action<TokenUsageContext>? TokenUsed;
}