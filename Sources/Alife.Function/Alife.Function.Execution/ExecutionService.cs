using System;
using Alife.Foundation;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Client;

namespace Alife.Function.Execution;

[Module("脚本执行",
    "让AI拥有在设备上运行命令的能力，从而执行任何任务！",
    defaultCategory: "Alife 官方/实用工具")]
public partial class ExecutionService(
    XmlFunctionCaller functionCaller,
    Interactor<ExecutionService> interactor,
    IAlifeApplication alifeApplication) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.Content)]
    [Description("执行python脚本")]
    public async Task Python(XmlExecutorContext context, [XmlContent] string script,
        [Description("预估运行时间（单位秒）")] int timeout, CancellationToken cancellationToken)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string filePath = $"{AlifePath.TempFolderPath}/PythonService-{Guid.NewGuid()}.py";
            await File.WriteAllTextAsync(filePath, context.FullContent.Trim(), cancellationToken);

            string result;
            try
            {
                result = await Python(alifeApplication, filePath, timeout, cancellationToken);
            }
            finally
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // ignored
                }
            }

            interactor.Poke("脚本执行完成\n" + result);
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("基于内部镜像和伪装的下载工具")]
    public async Task Download(string url, string path)
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

public partial class ExecutionService
{
    public static async Task<string> Python(IAlifeApplication alifeApplication, string path, int timeout = 30,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= 0) throw new ArgumentOutOfRangeException(nameof(timeout));
        using CancellationTokenSource timeCts = new(TimeSpan.FromSeconds(timeout));
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeCts.Token);
        try
        {
            using IAlifeProcess process = alifeApplication.StartPython(path);
            AlifeProcessResult result = await process.WaitCompletion(cts.Token);
            if (result.ExitCode != 0) throw new Exception(result.Output);
            return result.Output;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException("脚本执行堵塞或超时（注意不要写需要交互的代码，如果需要展示结果等可以单独创建一个进程）");
        }
    }
}
