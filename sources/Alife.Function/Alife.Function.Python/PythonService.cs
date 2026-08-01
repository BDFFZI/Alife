using System;
using Alife.Foundation;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Python;

[Module("脚本执行",
    "借助Python，让AI几乎可以执行任何任务！",
    defaultCategory: "Alife 官方/实用工具")]
public partial class PythonService(
    XmlFunctionCaller functionCaller,
    IInteractor<PythonService> interactor) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.Content)]
    [Description("执行python脚本")]
    public async Task Python(XmlExecutorContext context, [XmlContent] string script,
        [Description("预估运行时间（单位秒）")] int timeout, CancellationToken cancellationToken)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string filePath = $"{AlifePath.TempFolderPath}/pythonScript.py";

            await File.WriteAllTextAsync(filePath, context.FullContent.Trim(), cancellationToken);

            string result = await Python(filePath, timeout, cancellationToken);
            interactor.Poke("脚本执行完成\n" + result);
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    public async Task DownloadFile(string url, string path)
    {
        await AlifeUtility.DownloadFileAsync(url, path);
        interactor.Poke("已下载");
    }

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "利用python实现文件管理、设备控制、绘画演奏等各种复杂的自定义能力。",
            Explanation = "如果缺少环境可使用`subprocess.check_call([sys.executable, \"-m\", \"pip\", \"install\", package_name])`安装"
        };
        functionCaller.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);
        functionCaller.AddPlainAreas(nameof(Python));

        return Task.CompletedTask;
    }
}

public partial class PythonService
{
    public static async Task<string> Python(string path, int timeout = 30, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new() {
            FileName = "python",
            Arguments = $"\"{path}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using Process process = new();
        process.StartInfo = startInfo;
        process.Start();

        try
        {
            CancellationTokenSource timeCts = new(timeout * 1000);
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeCts.Token);

            // 同时开始读取输出和错误流，防止缓冲区满导致进程死锁
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode == 0)
                return await outputTask;
            else
                throw new Exception(await errorTask);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException("脚本执行堵塞或超时（注意不要写需要交互的代码，如果需要展示结果等可以单独创建一个进程）");
        }
        finally
        {
            if (process.HasExited == false)
                process.Kill();
        }
    }
}
