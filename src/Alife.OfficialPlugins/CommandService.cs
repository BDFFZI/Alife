namespace Alife.OfficialPlugins;

using System.Diagnostics;
using System.Text;
using Alife.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

[Plugin("命令行工具", "借助命令行工具，以极小的上下文成本让AI能做出各种复杂的事情！")]
public class CommandService : IPlugin
{
    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread context)
    {
        kernelBuilder.Plugins.AddFromObject(this);
        context.ChatHistory.AddSystemMessage(
            @"功能说明：
1. 在需要时调用Command命令，这几乎可以满足任何需求，所以当用户要求较高时，你可以直接使用Command！（有需要直接用，但也别没事乱用！）
2. 调用Command函数时，系统会在缓存文件夹创建一个名为‘name’，内容为‘body’的脚本，然后将其作为‘exec’的参数执行。
3. 由于输出文件是utf-8格式，而cmd,pwsh不支持中文，所以优先使用python执行命令（该环境默认预装，如果缺东西你也可以再装）。
4. 除非有显式窗口，否则用户看不到你的执行内容，这个功能只是给你调用命令用的，你直接用最简的方式直接执行，因为用户是无法插手的。
5. 再次强调，对于高要求主动尝试Command解决！再次强调！用最极简的方式写命令，不要带任何注释等无关内容，只干当前必要的事！

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
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();


        ChatMessage chatMessage = new ChatMessage() {
            isDefaultHiding = true,
            isInputting = true,
            author = string.Join(" ", exec, name),
            content = content
        };
        chatWindow.AddMessage(chatMessage);

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            output = await process.StandardError.ReadToEndAsync();

        chatMessage.isInputting = false;
        chatMessage.content += "\n\n执行结果：\n" + output;
        chatWindow.UpdateMessage(chatMessage);

        if (process.ExitCode != 0)
            throw new Exception(output);
        return output;
    }

    readonly StorageSystem storageSystem;
    readonly ChatWindow chatWindow;

    public CommandService(StorageSystem storageSystem, ChatWindow chatWindow)
    {
        this.storageSystem = storageSystem;
        this.chatWindow = chatWindow;
    }
}
