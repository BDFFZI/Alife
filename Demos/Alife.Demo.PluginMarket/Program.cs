using Alife.PluginMarket;

string pluginInstalledDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Alife.Plugins_Installed");
string packageListFile = Path.Combine(pluginInstalledDir, "NUGET_PACKAGES.txt");

Directory.CreateDirectory(pluginInstalledDir);

// ============ 创建PluginMarket ============
ZipPluginProvider provider = new("https://github.com/BDFFZI/Alife.PluginMarket/archive/refs/heads/main.zip");
FileSystemPluginInstaller installer = new(pluginInstalledDir);

Dictionary<string, IEnvironmentInstaller> environmentInstallers = new()
{
    { "pip", new PipEnvironmentInstaller() },
    { "nuget", new NuGetEnvironmentInstaller(packageListFile) }
};

PluginMarket market = new PluginMarket(provider, installer, installer, environmentInstallers, pluginInstalledDir);

// ============ 1. 在线插件列表 ============
Console.WriteLine("=== 在线插件列表 ===");
PluginPackage[] onlinePlugins = await provider.GetPluginsAsync();
Console.WriteLine($"  找到 {onlinePlugins.Length} 个在线插件\n");

// ============ 2. 测试安装 Mcp（依赖FunctionCaller + nuget包） ============
Console.WriteLine("=== 测试安装 Mcp（依赖FunctionCaller + nuget包） ===");
try
{
    PluginPackage? mcp = onlinePlugins.FirstOrDefault(p => p.Id == "Alife.Function.Mcp");
    if (mcp != null)
    {
        Console.WriteLine($"安装 {mcp.Id} v1.0.0...");
        await market.InstallPlugin(mcp, "1.0.0");
        Console.WriteLine("安装成功");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"安装失败: {ex.Message}");
}
Console.WriteLine();

// ============ 3. 查看已安装插件列表 ============
Console.WriteLine("=== 已安装插件列表 ===");
Dictionary<string, string> installedPlugins = installer.GetPlugins();
Console.WriteLine($"  找到 {installedPlugins.Count} 个已安装插件");
foreach (var kvp in installedPlugins)
{
    Console.WriteLine($"  [{kvp.Key}] v{kvp.Value}");
}

// ============ 4. 查看nuget包目录列表 ============
Console.WriteLine("\n=== nuget包目录 ===");
var nugetInstaller = new NuGetEnvironmentInstaller(packageListFile);
(string[] managed, string[] unmanaged) = nugetInstaller.ReadPackageList();
string[] packagePaths = managed;
Console.WriteLine($"  找到 {packagePaths.Length} 个包");
foreach (string path in packagePaths)
{
    Console.WriteLine($"    {path}");
}
