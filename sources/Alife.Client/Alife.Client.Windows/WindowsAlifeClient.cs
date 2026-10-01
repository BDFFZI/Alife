using ElectronNET.API;
using ElectronNET.API.Entities;
using MessageBoxOptions = ElectronNET.API.Entities.MessageBoxOptions;

namespace Alife.Client;

public class WindowsAlifeClient : IAlifeClient
{
    public void Quit()
    {
        var options = new MessageBoxOptions("是否直接关闭应用？") {
            Title = "Alife",
            Buttons = ["取消", "是 - 直接关闭", "否 - 最小化到托盘"],
            Type = MessageBoxType.question,
        };
        var result = Electron.Dialog.ShowMessageBoxAsync(Program.MainWindow, options).Result;
        if (result.Response == 1) Electron.App.Exit();
        else if (result.Response == 2) Program.MainWindow.Hide();
    }
    public void Exit()
    {
        Electron.App.Exit();
    }
    public async Task<string?> ShowSelectDirectoryDialog()
    {
        var browserWindow = Electron.WindowManager.BrowserWindows.First();
        var options = new OpenDialogOptions {
            Title = "请选择文件夹",
            Properties = [OpenDialogProperty.openDirectory]
        };
        string[] result = await Electron.Dialog.ShowOpenDialogAsync(browserWindow, options);
        return result.FirstOrDefault();
    }
}