using System;
using System.IO;
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

        //每次清空缓存
        string tempFolderPath = Instance.TempFolderPath;
        Directory.Delete(tempFolderPath, recursive: true);
        Directory.CreateDirectory(tempFolderPath);

        AlifeLog.Initialize(Instance); //初始化静态日志系统
        AlifeMirror.Initialize(); //初始化网络镜像系统
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