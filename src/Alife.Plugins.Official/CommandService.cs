using System.Diagnostics;
using Alife;
using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

[Plugin("命令行工具", "借助命令行工具，以极小的上下文成本让AI能做出各种复杂的事情！")]
public class CommandService : IPlugin
{
    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread agentThread)
    {
        kernelBuilder.Plugins.AddFromObject(this);
        agentThread.ChatHistory.AddSystemMessage(
            @"功能说明：
1. 你可以在需要时调用Command命令，因为其对接命令行，几乎可以满足你的任何需求！（但别没事乱用！）
2. 调用Command函数时，系统会在缓存文件夹创建一个名为‘name’，内容为‘body’的脚本，然后将其作为‘exec’的参数执行

使用案例：
- 案例一：
    执行：Command('test.py', 'print(1+1)', 'python')
    输出：2
    细节：系统在缓存文件夹创了一个'test.py'文件，其内容为'print(1+1)'，然后系统调用'python test.py'执行了该脚本。
（注意，上述操作依赖python，你可能需要先检查python环境，例如下面这个案例）
- 案例二：
    执行：Command('check.bat', 'python -V', '')
    输出：Python 3.14.2
    细节：系统创建了'check.bat'文件，内容为'python -V'，由于'exec'没有提供，因此系统将直接调用'check.bat'。
"
        );
        return Task.CompletedTask;
    }

    [KernelFunction]
    public async Task<string> Command(string name, string content, string exec)
    {
        string filePath = storageSystem.GetTempPath(name);
        await File.WriteAllTextAsync(filePath, content);

        bool hasExec = string.IsNullOrWhiteSpace(exec) == false;
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = hasExec ? exec : filePath,
            Arguments = hasExec ? filePath : null,
            UseShellExecute = false, // 必须设为 false 才能重定向流
            RedirectStandardOutput = true, // 重定向标准输出
            RedirectStandardError = true, // 重定向错误输出
            CreateNoWindow = true,
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync();
            throw new Exception(error);
        }

        return output;
    }

    readonly StorageSystem storageSystem;

    public CommandService(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
    }
}
