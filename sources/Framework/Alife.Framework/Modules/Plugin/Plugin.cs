using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace Alife.Framework;

public abstract class Plugin
{
    public struct AwakeContext
    {
        public Character character;
        public IKernelBuilder kernelBuilder;
        public ChatHistoryAgentThread contextBuilder;
    }

    public virtual Task AwakeAsync(AwakeContext context)
    {
        return Task.CompletedTask;
    }
    public virtual Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        return Task.CompletedTask;
    }
    public virtual Task DestroyAsync()
    {
        return Task.CompletedTask;
    }
}
