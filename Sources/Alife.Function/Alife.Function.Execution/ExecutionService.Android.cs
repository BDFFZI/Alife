#if ANDROID
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.FunctionCaller;
using Alife.Client.Android;

namespace Alife.Function.Execution;

public partial class ExecutionService
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("在Android上执行 ShizukuShell")]
    public async Task Shell(string command, CancellationToken cancellationToken = default)
    {
        AndroidAlifePlatform.ShellResult result = await AndroidAlifePlatform.ExecuteShellAsync(command, cancellationToken);
        interactor.Poke($"退出码: {result.ExitCode}\n{result.Output}");
    }
}
#endif
