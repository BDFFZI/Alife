using System;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Foundation;

public static class AlifeFoundation
{
    public static IAlifeFoundation Instance { get; private set; } = null!;

    public static void AddAlifeFoundation<TAlifeFoundation>(this IServiceCollection services)
        where TAlifeFoundation : class, IAlifeFoundation
    {
        services.AddSingletonChain<IAlifeFoundation, TAlifeFoundation>();
    }
    public static void InitAlifeFoundation(this IServiceProvider provider)
    {
        AlifeLog.Initialize(); //激活静态日志功能
        AlifeMirror.Initialize(); //激活网络镜像功能
        Instance = provider.GetRequiredService<IAlifeFoundation>();
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
}