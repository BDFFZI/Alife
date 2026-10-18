using System.Text;
using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client.Android;

public sealed class AndroidPipEnvironmentInstaller : PipEnvironmentInstaller
{
    protected override async Task InstallRequirements(IEnumerable<KeyValuePair<string, string>> items)
    {
        KeyValuePair<string, string>[] packages = items.ToArray();
        if (packages.Length == 0)
            return;

        if (ShizukuBridge.IsAvailable == false || ShizukuBridge.HasPermission == false)
        {
            AlifeLog.LogWarning("Shizuku 未连接或未授权，已跳过 pip 环境安装。");
            return;
        }
        if (ShizukuPythonInstaller.IsInstalled == false)
        {
            AlifeLog.LogWarning("未安装 Python，已跳过 pip 环境安装。相关插件在运行时可能不可用。");
            return;
        }

        string requirements = string.Join("\n", packages.Select(package => $"{package.Key}{package.Value}")) + "\n";
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(requirements));
        string requirementsPath = Path.Combine(ShizukuBridge.PythonRoot, "PipPackages.txt").Replace('\\', '/');

        ShizukuShellResult writeResult = await ShizukuBridge.ExecuteAsync(
            $"echo '{encoded}' | base64 -d > '{requirementsPath}'");
        if (writeResult.ExitCode != 0)
            throw new InvalidOperationException($"写入 pip 依赖清单失败：{writeResult.Output}");

        string command = $"""
                           export HOME={ShizukuBridge.PythonRoot}
                           export TMPDIR={ShizukuBridge.PythonRoot}/tmp
                           export PYTHONHOME={ShizukuBridge.PythonUsr}
                           export LD_LIBRARY_PATH={ShizukuBridge.PythonUsr}/lib
                           export PATH={ShizukuBridge.PythonUsr}/bin:$PATH
                           export SSL_CERT_FILE={ShizukuBridge.PythonUsr}/etc/tls/cert.pem
                           mkdir -p {ShizukuBridge.PythonRoot}/tmp
                           cd {ShizukuBridge.PythonRoot}
                           ./usr/bin/python -m pip install --no-cache-dir --disable-pip-version-check -r '{requirementsPath}' 2>&1
                           """;
        ShizukuShellResult result = await ShizukuBridge.ExecuteAsync(command);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pip 环境安装失败（退出码 {result.ExitCode}）：\n{result.Output}");

        AlifeLog.LogInformation($"pip 环境安装完成：{string.Join(", ", packages.Select(package => package.Key))}");
    }
}
