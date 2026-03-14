using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace Alife.Live2D.Test;

public partial class App : Application
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string BaseUrl = "http://localhost:5001";
    private Process? _petProcess;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Task.Run(async () => {
            try 
            {
                // 1. 尝试启动桌宠进程
                await LaunchPetAppAsync();

                // 2. 等待服务就绪
                await WaitForServiceReadyAsync();

                // 3. 执行测试指令
                await RunTestsAsync();
                
                Dispatcher.Invoke(() => MessageBox.Show("测试指令发送完毕！"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"测试环境自动化配置失败:\n{ex.Message}\n\n堆栈: {ex.StackTrace}"));
            }
            finally
            {
                // 测试结束后关闭测试端 (不关闭桌宠)
                // Dispatcher.Invoke(() => Shutdown());
            }
        });
    }

    private async Task LaunchPetAppAsync()
    {
        string projectDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] possiblePaths = new[] {
            Path.GetFullPath(Path.Combine(projectDir, "..", "..", "..", "..", "Alife.Live2D", "bin", "Debug", "net10.0-windows", "Alife.Live2D.exe")),
            Path.GetFullPath(Path.Combine(projectDir, "Alife.Live2D.exe")),
            Path.GetFullPath(Path.Combine(projectDir, "Alife.Live2D", "Alife.Live2D.exe")),
            Path.GetFullPath(Path.Combine(projectDir, "..", "Alife.Live2D", "Alife.Live2D.exe"))
        };

        string? petExePath = null;
        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                petExePath = path;
                break;
            }
        }

        if (petExePath == null)
        {
            string searchedPaths = string.Join("\n", possiblePaths);
            throw new FileNotFoundException($"找不到桌宠可执行文件。已搜索路径：\n{searchedPaths}");
        }

        // 检查是否已经在运行
        var runningProcesses = Process.GetProcessesByName("Alife.Live2D");
        if (runningProcesses.Length > 0)
        {
            return;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = petExePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(petExePath)
        };

        try 
        {
            _petProcess = Process.Start(startInfo);
            if (_petProcess == null) throw new Exception("Process.Start 返回 null");
        }
        catch (Exception ex)
        {
            throw new Exception($"启动桌宠进程失败: {ex.Message}", ex);
        }
    }

    private async Task WaitForServiceReadyAsync()
    {
        int retries = 15;
        while (retries-- > 0)
        {
            try 
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch 
            {
                // 忽略异常，继续重试
            }
            await Task.Delay(1000);
        }
        throw new TimeoutException("等待服务启动超时（5001端口）。请检查桌宠是否启动成功。");
    }

    private async Task RunTestsAsync()
    {
        await Task.Delay(1000);
        await SendCommand("/say?text=哈哈，环境已经全自动配置好啦！");
        await Task.Delay(3000);
        await SendCommand("/action?name=开心");
        await Task.Delay(2000);
        await SendCommand("/say?text=测试完成！");
    }

    private async Task SendCommand(string path)
    {
        var response = await _httpClient.GetAsync(BaseUrl + path);
        response.EnsureSuccessStatusCode();
    }
}
