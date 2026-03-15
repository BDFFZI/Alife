namespace Alife.OfficialPlugins;

using System.Diagnostics;
using System.Text;
using Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

[Plugin("Python工具", "借助Python，让AI几乎可以执行任何任务！")]
public class PythonService : IPlugin
{
    public Task AwakeAsync(IKernelBuilder kernelBuilder, ChatHistoryAgentThread context)
    {
        kernelBuilder.Plugins.AddFromObject(this);
        context.ChatHistory.AddSystemMessage(
            @"PythonService
功能说明：
1. 当用户要求较高时，直接尝试用Python实现目标，借助Python你几乎可用做任何事（如调用命令，文件读写，网页访问，以及丰富的Python库）
2. 如果Python中缺少环境之类，直接自行处理，例如升级或安装。这个Python环境完全由你管理，用户不是程序员，你要替他做事。
3. 用户看不到你的执行内容，除了窗口应用无法交互。所以你要用极简的代码直接完成你的需求，不要去等待用户响应或写无关的注释等。
4. 不要询问用户与Python相关的内容，用户不懂这些，所以有需要你直接用，优先保持对话的自然性（但也别没事乱用！）"
        );
        return Task.CompletedTask;
    }

    [KernelFunction]
    public async Task<string> Python(string content)
    {
        string filePath = storageSystem.GetTempPath("pythonScript.py");
        await File.WriteAllTextAsync(filePath, content);
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = "python",
            Arguments = filePath,
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
            author = nameof(PythonService),
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

    public PythonService(StorageSystem storageSystem, ChatWindow chatWindow)
    {
        this.storageSystem = storageSystem;
        this.chatWindow = chatWindow;
    }
}
