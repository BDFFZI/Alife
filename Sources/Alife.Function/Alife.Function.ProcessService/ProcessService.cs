using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.ProcessService;

[Module("进程操作",
    "提供进程创建、管理和管道通信能力",
    defaultCategory: "Alife 官方/实用工具")]
public class ProcessService(
    XmlFunctionCaller functionCaller,
    Interactor<ProcessService> interactor) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.OneShot)]
    public void CreateProcess(
        [Description("进程名称(要兼具唯一性和可读性)")] string name,
        string command,
        string? arguments = null,
        string? workdir = null)
    {
        ProcessInfo info = impl.CreateProcess(command, name, arguments, workdir);
        interactor.Poke($"进程已创建: {info.Name} (PID: {info.Id})");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void KillProcess(string name)
    {
        if (impl.KillProcess(name) == false)
            throw new Exception($"进程不存在: {name}");

        interactor.Poke($"进程已杀死: {name}");
    }

    [XmlFunction(FunctionMode.Content)]
    public async Task WriteProcess(
        XmlExecutorContext context,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        await impl.WriteAsync(name, context.FullContent, cancellationToken);
        interactor.Poke($"已写入: {name}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public async Task ReadProcess(
        string name,
        [Description("等待秒数")] int waiting = 2,
        [Description("最大读取行数")] int maxlines = 100,
        CancellationToken cancellationToken = default)
    {
        interactor.Poke(await impl.ReadOutputAsync(name, waiting, maxlines, cancellationToken));
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ClearProcess(string name)
    {
        impl.ClearOutput(name);
        interactor.Poke($"已清除进程输出缓冲: {name}");
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ListProcesses()
    {
        interactor.Poke(impl.ListProcesses());
    }

    readonly ProcessManagerImpl impl = new(new SystemProcessFactory());

    protected override Task OnAwake()
    {
        TryCreateDefaultProcess("python", "python", "-X utf8 -u -i");
        TryCreateDefaultProcess("pwsh", "pwsh");

        XmlHandler xmlHandler = new(this) {
            Description = "当你需要启动外部进程并持续通讯时使用",
            Explanation = """
                          提供创建管理进程并通过管道于其通讯的功能，可以借此实现代码执行，电脑管理，调用外部应用的能力

                          预设进程
                          模块启动时会自动创建以下持久进程，可直接使用：
                          - `python`（基于如下指令`python -X utf8 -u -i`创建）
                          - `pwsh`

                          用法示例
                           ```
                           <clearprocess name="python"/> # 先清空进程输出流，防止被上次内容干扰
                           <writeprocess name="python">a=1;print(a)</writeprocess> # 使用python进程执行代码
                           <readprocess name="python" waiting="2" maxlines="10"/>   # 等待2秒，最多读取10行，结果为1
                           <writeprocess name="python">a+=1;print(a)</writeprocess> # 进程可以持续交互
                           <readprocess name="python" waiting="1"/> # python变量累加，结果为2
                           ```

                          使用提示
                          - 避免执行阻塞操作，否则进程可能会被卡死。如果进程异常，可以尝试杀死重新创建。创建进程时注意设置编码为 UTF-8
                          - 如果要执行长代码，应先写入外部脚本文件，然后通过调用脚本的方式来执行，从而避免管道环境的语法问题并方便后续更改
                          - 当进程不使用时要记得杀死进程
                          """
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);
        functionCaller.AddPlainAreas(nameof(WriteProcess));

        return Task.CompletedTask;
    }
    protected override Task OnDestroy()
    {
        impl.KillAll();

        return Task.CompletedTask;
    }

    void TryCreateDefaultProcess(string name, string command, string? arguments = null)
    {
        try
        {
            impl.CreateProcess(command, name, arguments);
        }
        catch
        {
            // 可执行文件不存在等，静默跳过
        }
    }
}