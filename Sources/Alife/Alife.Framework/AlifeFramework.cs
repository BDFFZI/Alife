using Microsoft.Extensions.DependencyInjection;

namespace Alife.Framework;

public static class AlifeFramework
{
    public static void AddAlifeFramework(this IServiceCollection services)
    {
        //装载基本框架
        services.AddSingleton<StorageSystem>();
        services.AddSingleton<ConfigurationSystem>();
        services.AddSingleton<ModuleSystem>();
        services.AddSingleton<CharacterSystem>();
        services.AddSingleton<ChatActivitySystem>();
    }
}