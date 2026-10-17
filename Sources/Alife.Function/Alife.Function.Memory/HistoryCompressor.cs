using System.Threading.Tasks;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

public abstract class HistoryCompressor
{
    public abstract Task<string?> Compress(ChatHistoryAgentThread chatHistoryAgentThread, string prompt);
}
