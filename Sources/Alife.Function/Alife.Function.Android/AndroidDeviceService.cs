using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
#if ANDROID
using Alife.Client.Android;
#endif

namespace Alife.Function.Android;

[Module("Android 设备控制",
    "通过 Shizuku 以 shell 身份管理手机，可执行命令、查询连接与授权状态。",
    defaultCategory: "Alife 官方/设备控制")]
public class AndroidDeviceService(
    XmlFunctionCaller functionCaller,
    Interactor<AndroidDeviceService> interactor) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("执行一条 Android shell 命令（Shizuku shell 身份），返回退出码和合并后的输出。")]
    public async Task Shell(
        [Description("要执行的 shell 命令，例如：pm list packages | head")] string command,
        CancellationToken cancellationToken = default)
    {
#if ANDROID
        ShizukuShellResult result = await ShizukuBridge.ExecuteAsync(command, cancellationToken);
        interactor.Poke($"退出码: {result.ExitCode}\n{result.Output}");
#else
        throw new PlatformNotSupportedException("Android 设备控制仅在 Android 客户端可用。");
#endif
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看 Shizuku 连接与授权状态。")]
    public void Status()
    {
#if ANDROID
        interactor.Poke($"Shizuku 可用: {ShizukuBridge.IsAvailable}\n已授权: {ShizukuBridge.HasPermission}\n身份 UID: {ShizukuBridge.Uid}");
#else
        interactor.Poke("当前平台不支持 Shizuku。");
#endif
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("请求 Shizuku 授权，会弹出 Shizuku 授权确认。")]
    public async Task RequestPermission()
    {
#if ANDROID
        bool granted = await ShizukuBridge.RequestPermissionAsync();
        interactor.Poke(granted ? "Shizuku 授权成功。" : "Shizuku 授权未通过。");
#else
        throw new PlatformNotSupportedException("Android 设备控制仅在 Android 客户端可用。");
#endif
    }

    protected override Task OnAwake()
    {
        XmlHandler handler = new(this) {
            Description = "用于管理 Android 手机。通过 Shizuku 以 shell 身份执行命令，例如查看安装包、修改设置、模拟输入、抓取界面等。"
        };
        functionCaller.RegisterHandler(handler, DocumentMode.Implicit, DestroyCancellationToken);
        return Task.CompletedTask;
    }
}
