using Alife.Foundation;
using Alife.Framework;
using Microsoft.Extensions.DependencyInjection;

namespace Alife.Client;

public static class AlifeClient
{
    public static void AddAlifeClient<TAlifeFoundation, TCSharpCompiler,
        TNuGetEnvironmentInstaller, TPipEnvironmentInstaller,
        TAlifeApplication, TAlifeClient>(
        this IServiceCollection services)
        where TAlifeFoundation : class, IAlifeFoundation
        where TCSharpCompiler : CSharpCompiler
        where TNuGetEnvironmentInstaller : NuGetEnvironmentInstaller
        where TPipEnvironmentInstaller : PipEnvironmentInstaller
        where TAlifeApplication : class, IAlifeApplication
        where TAlifeClient : class, IAlifeClient
    {
        services.AddAlife<TAlifeFoundation, TCSharpCompiler, TNuGetEnvironmentInstaller, TPipEnvironmentInstaller, TAlifeApplication>(); //alife框架

        //客户端功能
        services.AddSingletonChain<IAlifeClient, TAlifeClient>();
        services.AddSingleton<AlifeClientConfig>();
        services.AddSingleton<ClientDataStore>();
    }
    public static async Task InitAlifeClient(this IServiceProvider provider, IProgress<string>? progress = null)
    {
        //初始化前回调
        {
            IAlifeClientInitialize? initialize = provider.GetService<IAlifeClientInitialize>();
            if (initialize != null) await initialize.OnInitializeAsync(progress);
        }

        AlifeClientConfig config = provider.GetRequiredService<AlifeClientConfig>();
        await provider.InitAlife(config, progress);

        if (config.FetchOnlineInfo)
        {
            progress?.Report("正在检测客户端更新...");
            try
            {
                await ClientUpgrade.FetchNewVersion();
            }
            catch (Exception e)
            {
                AlifeLog.LogError("客户端更新检测失败：" + e);
            }
        }

        //额外的客户端功能
        {
            progress?.Report("初始化客户端功能...");

            ChatActivitySystem chatActivitySystem = provider.GetRequiredService<ChatActivitySystem>();
            StorageSystem storageSystem = provider.GetRequiredService<StorageSystem>();

            //初始化对话统计功能
            ClientChatStatistics.Initialize(chatActivitySystem, storageSystem);
            //增加额外的基于客户端的功能
            chatActivitySystem.InjectedObjects.Add(provider.GetRequiredService<IAlifeClient>());
        }

        //初始化后回调
        {
            IAlifeClientInitialized? initialized = provider.GetService<IAlifeClientInitialized>();
            if (initialized != null) await initialized.OnInitializedAsync(progress);
        }
    }
}