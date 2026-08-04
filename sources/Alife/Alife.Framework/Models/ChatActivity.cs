using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;
using Microsoft.Extensions.Logging;

namespace Alife.Framework;

public class ChatActivity(
    Character character,
    ConfigurationSystem configurationSystem,
    ModuleSystem moduleSystem,
    CharacterSystem characterSystem,
    object[]? appendServices = null)
{
    public Character Character => character;
    public ChatBot ChatBot { get; private set; } = null!;

    /// <summary>
    /// 为了支持插件热重载，请不要缓存其中的对象类型，因为重载后便会失效
    /// </summary>
    public ConstructContainer Container => container;

    public async Task Awake(IProgress<(string, float)>? progress = null)
    {
        //解析用户启用的模块
        Type[] enabledModuleTypes = character.Modules
            .Select(moduleSystem.GetModule)
            .Where(t => t != null).Cast<Type>()
            .OrderBy(t => ModuleSystem.GetModuleAttribute(t)!.LaunchOrder)
            .ToArray();

        //创建基础环境
        {
            //创建服务容器
            container = new();
            //添加现有系统
            if (appendServices != null)
            {
                foreach (var appendService in appendServices)
                    await container.AddInstance(appendService);
            }

            //添加可选工具
            {
                //logger功能
                container.RegisterBuilder(typeof(LoggerFactory), _ =>
                    Task.FromResult<object>(LoggerFactory.Create(builder => {
                        builder.SetMinimumLevel(LogLevel.Information);
                        builder.AddProvider(new AlifeLogProvider());
                    }))
                );
                container.RegisterBuilder(typeof(Logger<>), isSingleton: false);
                //interactor功能
                container.RegisterBuilder(typeof(Interactor<>), isSingleton: false);
            }
            //添加用户模块（先插入优先级高）
            foreach (Type moduleType in enabledModuleTypes)
                container.RegisterBuilder(moduleType);
            //添加全部模块（后备模块）
            foreach (Type moduleType in moduleSystem.GetAllModules())
            {
                if (enabledModuleTypes.Contains(moduleType) == false)
                    container.RegisterBuilder(moduleType);
            }

            //创建chatbot
            progress?.Report(($"构造 {TypeUtility.GetReadableName(typeof(ChatBot))} 模块", 0));
            container.RegisterBuilder(typeof(ChatBot));
            ChatBot = (ChatBot)await container.RequireInstance(typeof(ChatBot));
            //填充人设
            ChatBot.ChatHistory.AddSystemMessage(
                $"""
                 这是你的人物信息：
                 - 名称：{character.Name}
                 - 生日：{character.Birthday}
                 - 简介：{character.Description}
                 - 设定：
                 {character.Prompt}

                 这是你的私人文件夹：
                 {Path.Combine(AlifePath.StorageFolderPath, Character.StorageKey, "Storage")}
                 """);
            ChatBot.UpdateHistoryEndIndex();

            //创建用户显式启用的模块
            for (int index = 0; index < enabledModuleTypes.Length; index++)
            {
                Type moduleType = enabledModuleTypes[index];
                progress?.Report(($"构造 {TypeUtility.GetReadableName(moduleType)} 模块", (float)index / enabledModuleTypes.Length));
                await container.RequireInstance(moduleType);
            }
        }

        //激活模块
        {
            AwakeContext awakeContext = new() {
                Character = character,
                ChatBot = ChatBot,
                ChatActivity = this
            };

            for (int index = 0; index < container.Instances.Count; index++)
            {
                object instance = container.Instances[index];
                progress?.Report(($"激活 {TypeUtility.GetReadableName(instance.GetType())} 模块", (float)index / container.Instances.Count));
                await OnInstanceCreated(instance);//处理一下已创建物体
            }

            container.InstanceCreated += OnInstanceCreated;

            async Task OnInstanceCreated(object instance)
            {
                if (instance is IConfigurable configurable)
                {
                    object configData = configurationSystem.GetConfiguration(instance.GetType(), character.StorageKey)!;
                    configurable.Configuration = configData;
                }

                if (instance is ChatBehaviour behaviour)
                {
                    await behaviour.AwakeAsync(awakeContext);
                }
            }
        }

        moduleSystem.ModulesLoadedAsync += OnModulesLoadedAsync;
        moduleSystem.ModulesUnloadedAsync += OnModulesUnloadedAsync;
        characterSystem.CharacterChangedAsync += OnCharacterChangedAsync;
    }

    public async Task Start(IProgress<(string, float)>? progress = null)
    {
        //启动模块
        {
            ChatBehaviour[] behaviours = container.Instances.OfType<ChatBehaviour>().ToArray();
            for (int index = 0; index < behaviours.Length; index++)
            {
                ChatBehaviour chatBehaviour = behaviours[index];
                progress?.Report(($"启动 {TypeUtility.GetReadableName(chatBehaviour.GetType())} 模块", (float)index / behaviours.Length));
                await chatBehaviour.UpdateAsync(new UpdateContext());
            }
        }
        //开始update
        cancelTimerSource = new CancellationTokenSource();
        StartTimer(1f, cancelTimerSource.Token);
    }
    public async Task Destroy(IProgress<(string, float)>? progress = null)
    {
        moduleSystem.ModulesLoadedAsync -= OnModulesLoadedAsync;
        moduleSystem.ModulesUnloadedAsync -= OnModulesUnloadedAsync;
        characterSystem.CharacterChangedAsync -= OnCharacterChangedAsync;

        if (cancelTimerSource != null)
            await cancelTimerSource.CancelAsync();
        await container.DisposeAsync(progress);
    }

    ConstructContainer container = null!;
    CancellationTokenSource? cancelTimerSource;

    async void StartTimer(float expectedDeltaTime, CancellationToken cancellationToken = default)
    {
        try
        {
            DateTime startTime = DateTime.Now;
            DateTime lastTime = DateTime.Now;
            int frameIndex = 0;
            float time = 0;

            while (cancellationToken.IsCancellationRequested == false)
            {
                //避免真的每帧更新
                await Task.Delay(TimeSpan.FromSeconds(expectedDeltaTime), cancellationToken);

                DateTime currentTime = DateTime.Now;
                float deltaTime = (float)(currentTime - lastTime).TotalSeconds;
                lastTime = currentTime;
                UpdateContext context = new() {
                    FrameCount = ++frameIndex,
                    RealTime = (float)(startTime - currentTime).TotalSeconds,
                    ExpectedDeltaTime = expectedDeltaTime,
                    DeltaTime = deltaTime,
                    Time = time += deltaTime,
                };

                foreach (ChatBehaviour behaviour in container.Instances.OfType<ChatBehaviour>())
                    await behaviour.UpdateAsync(context);
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    async Task OnModulesUnloadedAsync(List<Type> moduleTypes)
    {
        foreach (Type moduleType in moduleTypes)
            container.UnRegisterBuilder(moduleType);
        object[] invalidModules = container.Instances.Where(instance => moduleTypes.Contains(instance.GetType())).ToArray();
        foreach (object instance in invalidModules)
            container.RemoveInstance(instance);

        await TypeUtility.DisposeObjects(invalidModules);
    }
    async Task OnModulesLoadedAsync(List<Type> moduleTypes)
    {
        Type[] enabledModuleTypes = moduleTypes.Where(type => Character.Modules.Contains(ModuleSystem.GetModuleID(type))).ToArray();

        foreach (Type moduleType in enabledModuleTypes)
            container.RegisterBuilder(moduleType);
        foreach (Type moduleType in enabledModuleTypes)
            await container.RequireInstance(moduleType);
    }
    async Task OnCharacterChangedAsync(Character reloadedCharacter)
    {
        if (reloadedCharacter != character)
            return;

        Type[] enabledModuleTypes = reloadedCharacter.Modules
            .Select(moduleSystem.GetModule).Where(type => type != null).Cast<Type>()
            .Except(container.Instances.Select(instance => instance.GetType())).ToArray();
        foreach (Type moduleType in enabledModuleTypes)
            container.RegisterBuilder(moduleType);
        foreach (Type moduleType in enabledModuleTypes)
            await container.RequireInstance(moduleType);
    }
}
