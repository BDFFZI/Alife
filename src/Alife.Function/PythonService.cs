using System.ComponentModel;
using Alife.Interpreter;
using System.Diagnostics;
using System.Text;
using Alife.Abstractions;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

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
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            output = await process.StandardError.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(output) == false)
            chatBot.Poke("[来自系统的消息：这是刚刚Python的执行结果，你看是否有问题)]" + output);
    }

    readonly StorageSystem storageSystem;
    ChatBot chatBot = null!;

    public PythonService(StorageSystem storageSystem, InterpreterService interpreterService)
    {
        this.storageSystem = storageSystem;
        interpreterService.RegisterHandler(this);
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        chatBot = chatActivity.ChatBot;
        return Task.CompletedTask;
    }
}
