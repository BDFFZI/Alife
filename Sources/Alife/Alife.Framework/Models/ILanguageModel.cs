using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Framework;

[Description("语言模型")]
public interface ILanguageModel
{
    static readonly OccupationNotepad DefaultThinkingRequester = new();

    public OccupationNotepad GetThinkingRequester()
    {
        return DefaultThinkingRequester;
    }
    public Task<ChatResult> ChatStreamingAsync(
        ChatHistoryAgentThread chatHistoryAgentThread,
        Action<string>? textReceived = null,
        Action<string>? thinkReceived = null,
        CancellationToken cancellationToken = default
    );
}