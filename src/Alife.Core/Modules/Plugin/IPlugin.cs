using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Abstractions;

public interface IPlugin
{
    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread agentThread)
    {
        return Task.CompletedTask;
    }
    public Task StartAsync(Kernel kernel, ChatBot chatBot)
    {
        return Task.CompletedTask;
    }
    public Task DestroyAsync()
    {
        return Task.CompletedTask;
    }
}
