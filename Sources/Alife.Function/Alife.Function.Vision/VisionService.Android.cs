#if ANDROID
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Alife.Foundation;
using Alife.Function.FunctionCaller;
using Alife.Client.Android;

namespace Alife.Function.Vision;

public partial class VisionService
{
    /// <summary>
    /// 获取当前可以截取的所有可用窗口的列表，供 AI 选择截屏目标。
    /// </summary>
    [XmlFunction(FunctionMode.OneShot)]
    [Description("查询当前可见窗口，及其hwnd和焦点")]
    public async Task QueryWindows()
    {
        var result = await AndroidAlifePlatform.ExecuteShellAsync("dumpsys window windows | grep -E 'mCurrentFocus|mFocusedApp'", DestroyCancellationToken);
        interactor.Poke($"hwnd: -1 | Android 全屏截图（不支持独立窗口截图）\n当前焦点：{result.Output}");
    }

    async Task<string> CaptureAsync(long hwnd)
    {
        if (hwnd != -1) throw new NotSupportedException("Android 仅支持 hwnd=-1 的全屏截图。");
        string folder = Path.Combine(AlifePath.StorageFolderPath, "Temp", "vision");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"capture_{Guid.NewGuid():N}.png");
        var result = await AndroidAlifePlatform.ExecuteShellAsync($"screencap -p {("'" + path.Replace("'", "'\"'\"'") + "'")} 2>&1", DestroyCancellationToken);
        if (result.ExitCode != 0 || !File.Exists(path))
            throw new IOException($"Android 截图失败：{result.Output}");
        return path;
    }

    static string GetActiveWindowTitle() => "Android 前台应用";

    static Task<string> OcrAsync(string path) => Task.FromResult("Android 未提供本地 OCR，请使用视觉模型识别图片文字");
}
#endif
