using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Alife.Abstractions;
using Alife.Interpreter;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

[Plugin("Python工具", "借助Python，让AI几乎可以执行任何任务！")]
[Description(@"此服务能让你获得执行python的能力，可用于文件管理、设备控制、网页爬取等各自复杂的自定义需求。
如果缺少环境你还可以利用`subprocess.check_call([sys.executable, ""-m"", ""pip"", ""install"", package_name])`来安装环境。")]
public class PythonService : Plugin
{
    public event Action<string>? PrePythonRun;
    public event Action<string>? PostPythonRun;

    [XmlHandler]
    [Description(@"你的专属python执行器，如果执行有结果，还会在之后返回给你。
注意事项：
1. 永远把它作为最后一条指令执行，不要捏造结果，调用后请停止说话，并等待结果返回。
2. 主人看不到结果，包括其返回的结果，所以除非是窗口应用，不然不要让主人操作。
3. 注意一定要少写代码，能一行解决就不要两行，慎用，因为这个非常烧token，烧完你就宕机了！")]
    public async Task Python(XmlTagContext context)
    {
        if (context.Status != TagStatus.Closing)
            return;

        string filePath = storageSystem.GetTempPath("pythonScript.py");
        await File.WriteAllTextAsync(filePath, context.FullContent);
        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = PathEnvironment.PythonExecutablePath,
            Arguments = filePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        PrePythonRun?.Invoke(context.FullContent);

        using Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            output = await process.StandardError.ReadToEndAsync();

        PostPythonRun?.Invoke(output);

        if (string.IsNullOrWhiteSpace(output) == false)
            chatBot.Poke("[PythonService] Python执行结果如下：" + output);
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
