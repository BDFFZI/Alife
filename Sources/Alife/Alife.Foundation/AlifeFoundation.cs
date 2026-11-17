using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Foundation;

public static class AlifeFoundation
{
    public static void AddAlifeFoundation<TAlifeFoundation>(this IServiceCollection services)
        where TAlifeFoundation : class, IAlifeFoundation
    {
        services.AddSingletonChain<IAlifeFoundation, TAlifeFoundation>();
    }
    public static void InitAlifeFoundation(this IServiceProvider provider)
    {
        Instance = provider.GetRequiredService<IAlifeFoundation>();

        //每次清空缓存。
        //注意：上一次进程可能还没彻底退出（托盘未关、MCP 端口未释放），alife.log
        //仍被占用。旧的 Directory.Delete(recursive) 会在第一个被占文件上直接抛异常，
        //导致整个初始化中断、前端永远卡在启动页。这里改成容错清理：
        //能删就删，删不掉就改名隔离，绝不因此中断启动。
        string tempFolderPath = Instance.TempFolderPath;
        CleanTempDirectory(tempFolderPath);

        AlifeLog.Initialize(Instance); //初始化静态日志系统
        AlifeMirror.Initialize(); //初始化网络镜像系统
    }

    /// <summary>
    /// 容错清空临时目录：逐文件删除，遇到占用就改名隔离而不是硬删。
    /// 原则是「宁可留下一个占旧的日志副本，也不能让应用启动不了」。
    /// </summary>
    static void CleanTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) == false)
            {
                Directory.CreateDirectory(path);
                return;
            }

            //第一遍：常规删除。文件逐个删，目录再自底向上删，避免整目录删时被单文件拖垮。
            TryDeleteContents(path, quiet: true);

            //第二遍：对仍被占用的文件（典型是 alife.log），改名到 .stale 目录隔离。
            //改名在被占用文件上通常仍可行（Windows 允许 rename 未共享删除的打开文件，
            //只要不是独占），失败就彻底放弃这一项。
            string staleDir = Path.Combine(path, ".stale-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            bool staleCreated = false;

            foreach (string file in SafeEnumerateFiles(path))
            {
                try
                {
                    if (staleCreated == false)
                    {
                        Directory.CreateDirectory(staleDir);
                        staleCreated = true;
                    }
                    string target = Path.Combine(staleDir, Path.GetFileName(file));
                    //同名已存在则加序号，避免覆盖
                    int n = 1;
                    while (File.Exists(target))
                        target = Path.Combine(staleDir, Path.GetFileNameWithoutExtension(file) + "-" + n++ + Path.GetExtension(file));
                    File.Move(file, target);
                }
                catch
                {
                    //占用中的文件连改名都失败：留着它，AlifeLog 会以追加模式打开，
                    //不会让启动失败
                }
            }

            foreach (string dir in SafeEnumerateDirectories(path))
            {
                //别把刚建的隔离目录再扫进去
                if (string.Equals(dir, staleDir, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); }
                catch { }
            }

            Directory.CreateDirectory(path);
        }
        catch (Exception e)
        {
            //最后兜底：任何意外都不能中断启动，只记到控制台（此时日志系统尚未初始化）
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Warning] 清理临时目录失败，已跳过：" + e.Message);
            try { Directory.CreateDirectory(path); } catch { }
        }
    }

    static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static void TryDeleteContents(string path, bool quiet)
    {
        foreach (string file in SafeEnumerateFiles(path))
        {
            try { File.Delete(file); }
            catch { /*占用中，交给改名隔离那一步 */ }
        }
        foreach (string dir in SafeEnumerateDirectories(path))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /*交给下一步 */ }
        }
    }

    public static void AddSingletonChain<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(provider => provider.GetRequiredService<TImplementation>());
    }

    public static void AddSingletonChain<TInterface, TAbstract, TImplementation>(this IServiceCollection services)
        where TInterface : class
        where TAbstract : class, TInterface
        where TImplementation : class, TAbstract
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TAbstract>(provider => provider.GetRequiredService<TImplementation>());
        services.AddSingleton<TInterface>(provider => provider.GetRequiredService<TAbstract>());
    }

    internal static IAlifeFoundation Instance { get; set; } = null!;
}