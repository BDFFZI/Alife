using System.ComponentModel;
using Alife.Interpreter;

namespace Alife.OfficialPlugins;

using System.Diagnostics;
using System.Text;
using Alife.Abstractions;
using Microsoft.SemanticKernel;

[Plugin("Python工具", "借助Python，让AI几乎可以执行任何任务！")]
public class PythonService : Plugin
{
    [XmlHandler]
    [Description(@"你的专属python执行器（主人看不到噢~）
注意事项：
1. 不要捏造数据，不要模拟结果，不要写长脚本。
2. 用好了非常强大，但也很危险，妥善使用喵~")]
    public async Task Python(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing)
            return;

        string filePath = storageSystem.GetTempPath("pythonScript.py");
        await File.WriteAllTextAsync(filePath, context.FullContent);
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
            tool = nameof(PythonService),
            content = context.FullContent
        };
        dialogContext.AddMessage(chatMessage);

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            output = await process.StandardError.ReadToEndAsync();

        chatMessage.isInputting = false;
        chatMessage.content += "\n\n执行结果：\n" + output;
        dialogContext.UpdateMessage(chatMessage);

        if (string.IsNullOrWhiteSpace(output) == false)
            chatBot.Poke("[来自系统的消息：这是刚刚Python的执行结果，你看是否有问题)]" + output); //向ai反馈执行结果
    }


    // [KernelFunction]
    // public async Task<string> Python(string content)
    // {
    //     string filePath = storageSystem.GetTempPath("pythonScript.py");
    //     await File.WriteAllTextAsync(filePath, content);
    //     ProcessStartInfo startInfo = new ProcessStartInfo {
    //         FileName = "python",
    //         Arguments = filePath,
    //         UseShellExecute = false, // 必须设为 false 才能重定向流
    //         RedirectStandardOutput = true, // 重定向标准输出
    //         RedirectStandardError = true, // 重定向错误输出
    //         CreateNoWindow = true,
    //         Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
    //         StandardOutputEncoding = Encoding.UTF8,
    //         StandardErrorEncoding = Encoding.UTF8,
    //     };
    //
    //     using Process process = new Process();
    //     process.StartInfo = startInfo;
    //     process.Start();
    //
    //     ChatMessage chatMessage = new ChatMessage() {
    //         isDefaultHiding = true,
    //         isInputting = true,
    //         tool = nameof(PythonService),
    //         content = content
    //     };
    //     chatWindow.AddMessage(chatMessage);
    //
    //     string output = await process.StandardOutput.ReadToEndAsync();
    //     await process.WaitForExitAsync();
    //     if (process.ExitCode != 0)
    //         output = await process.StandardError.ReadToEndAsync();
    //
    //     chatMessage.isInputting = false;
    //     chatMessage.content += "\n\n执行结果：\n" + output;
    //     chatWindow.UpdateMessage(chatMessage);
    //
    //     if (process.ExitCode != 0)
    //         throw new Exception(output);
    //     return output;
    // }

    // [KernelFunction]
    // public async Task<string> Python(string content)
    // {
    //     string filePath = storageSystem.GetTempPath("pythonScript.py");
    //     await File.WriteAllTextAsync(filePath, content);
    //     ProcessStartInfo startInfo = new ProcessStartInfo {
    //         FileName = "python",
    //         Arguments = filePath,
    //         UseShellExecute = false, // 必须设为 false 才能重定向流
    //         RedirectStandardOutput = true, // 重定向标准输出
    //         RedirectStandardError = true, // 重定向错误输出
    //         CreateNoWindow = true,
    //         Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
    //         StandardOutputEncoding = Encoding.UTF8,
    //         StandardErrorEncoding = Encoding.UTF8,
    //     };
    //
    //     using Process process = new Process();
    //     process.StartInfo = startInfo;
    //     process.Start();
    //
    //     ChatMessage chatMessage = new ChatMessage() {
    //         isDefaultHiding = true,
    //         isInputting = true,
    //         tool = nameof(PythonService),
    //         content = content
    //     };
    //     chatWindow.AddMessage(chatMessage);
    //
    //     string output = await process.StandardOutput.ReadToEndAsync();
    //     await process.WaitForExitAsync();
    //     if (process.ExitCode != 0)
    //         output = await process.StandardError.ReadToEndAsync();
    //
    //     chatMessage.isInputting = false;
    //     chatMessage.content += "\n\n执行结果：\n" + output;
    //     chatWindow.UpdateMessage(chatMessage);
    //
    //     if (process.ExitCode != 0)
    //         throw new Exception(output);
    //     return output;
    // }

    readonly StorageSystem storageSystem;
    readonly DialogContext dialogContext;
    ChatBot chatBot = null!;

    public PythonService(StorageSystem storageSystem, DialogContext dialogContext, InterpreterService interpreterService)
    {
        this.storageSystem = storageSystem;
        this.dialogContext = dialogContext;

        interpreterService.RegisterHandler(this);
    }

    public override Task AwakeAsync(AwakeContext context)
    {
//         context.contextBuilder.ChatHistory.AddSystemMessage(
//             @"PythonService
// 功能说明：
// 1. 当用户要求较高时，直接尝试用Python实现目标，借助Python你几乎可用做任何事（如调用命令，文件读写，网页访问，以及丰富的Python库）
// 2. 如果Python中缺少环境之类，直接自行处理，例如升级或安装。这个Python环境完全由你管理，用户不是程序员，你要替他做事。
// 3. 用户看不到你的执行内容，除了窗口应用无法交互。所以你要用极简的代码直接完成你的需求，不要去等待用户响应或写无关的注释等。
// 4. 调用之前你一定要先向用户说明一下你需要准备脚本，因为用户很长时间将看不到你的回复，如果你不提前说明，会被认为是有故障。
// 5. 你可以在Python中执行命令行，例如`import os; os.system('python test.py')`，从而快速调用你写好的py脚本。
// "
//         );

        // context.kernelBuilder.Plugins.AddFromObject(this);

        return Task.CompletedTask;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        return Task.CompletedTask;
    }
}
