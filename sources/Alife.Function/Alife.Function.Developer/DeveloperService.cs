using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.Developer;

[Module("开发者模式",
    "向 AI 暴露项目和系统信息，并提供工具使其可以自制插件和活动管理。",
    defaultCategory: "Alife 官方/生活环境")]
public class DeveloperService(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    PluginSystem pluginSystem,
    ModuleSystem moduleSystem,
    XmlFunctionCaller functionCaller,
    ILogger<DeveloperService> logger,
    IInteractor<DeveloperService> interactor) :
    ChatBehaviour
{
    [XmlFunction(FunctionMode.OneShot)]
    public void ListCharactersInSystem()
    {
        var chars = characterSystem.GetAllCharacters();
        string result = string.Join("\n",
            chars.Select(character => $"{character.Name}:{(chatActivitySystem.IsActivated(character) ? "活跃中" : "未激活")}"));
        interactor.Poke(result);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void ListModulesInSystem()
    {
        var modules = moduleSystem.GetAllModules();
        string result = string.Join("\n", modules.Select(type => $"{ModuleSystem.GetModuleID(type)}:{ModuleSystem.GetModuleAttribute(type)!.Name}"));
        interactor.Poke(result);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void ListModulesInCharacter([Description("为空表示自己")] string? characterName = null)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
        {
            interactor.Poke("角色不存在");
            return;
        }

        string result = string.Join("\n", character.Modules
            .Select(module => $"{module}:{(moduleSystem
                .GetModule(module) != null ? "有效" : "模块不存在")}"));
        interactor.Poke(result);
    }

    [XmlFunction(FunctionMode.OneShot)]
    public async Task ReloadCharacters()
    {
        foreach (Character character in characterSystem.GetAllCharacters())
            await characterSystem.LoadCharacter(character);
        interactor.Poke("角色环境重载成功");
    }
    [XmlFunction(FunctionMode.OneShot)]
    [Description("当新增移动删除插件或修改了插件清单文件时调用，否则不需要。此功能主要用于同步nuget之类的插件环境依赖")]
    public async Task ReloadPluginEnvironment()
    {
        await pluginSystem.SyncLocalPluginEnvironment();
        interactor.Poke("插件环境同步成功");
    }
    [XmlFunction(FunctionMode.OneShot)]
    [Description("重新编译加载插件。当插件代码变动后需要调用来生效。")]
    public async Task ReloadPlugin(string pluginId)
    {
        await pluginSystem.ReloadPlugin(pluginId);
        interactor.Poke("插件重载成功");
    }
    [XmlFunction(FunctionMode.OneShot)]
    [Description("关闭对话程序然后重新打开。")]
    public void ReloadActivity([Description("为空表示自己")] string? characterName = null)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
        {
            interactor.Poke("角色不存在");
            return;
        }

        if (ChatBot.ChatHistory
            .Where(content => content.Role == AuthorRole.Assistant)
            .TakeLast(4).SkipLast(1).Any(content =>
                content.Content != null && content.Content.Contains(nameof(ReloadActivity), StringComparison.OrdinalIgnoreCase)))
        {
            interactor.Poke("不允许短时间多次重启，请等待几次后再试（你是否误解了重启含义和重启通知？）");
            return;
        }

        async void ActivateCharacter()
        {
            try
            {
                chatActivitySystem.ActivationFailed += OnActivationFailed;
                await chatActivitySystem.Activate(character);
                chatActivitySystem.ActivationFailed -= OnActivationFailed;

                Exception? ex = null;

                void OnActivationFailed(Character arg1, Exception arg2)
                {
                    ex = arg2;
                }

                //将结果传递给自己
                ChatActivity? chatActivity = chatActivitySystem.GetChatActivity(Character);
                if (chatActivity != null)
                    interactor.Poke($"{character.Name}激活{(ex == null ? "成功" : "失败\n" + ex)}");
            }
            catch (Exception e)
            {
                logger.LogError(e, "激活角色失败");
            }
        }

        if (chatActivitySystem.IsActivated(character))
            chatActivitySystem.Deactivate(character).ContinueWith(_ => ActivateCharacter());
        else
            ActivateCharacter();
    }

    [XmlFunction(FunctionMode.OneShot)]
    public void ReadDemoModuleCode()
    {
        interactor.Poke("""
                        using System;
                        using System.ComponentModel;
                        using System.Threading.Tasks;
                        using Alife.Framework;
                        using Alife.Function.FunctionCaller;
                        using Microsoft.Extensions.Logging;

                        namespace Alife.Demo.Plugin;

                        public class MyModuleConfig
                        {
                            [DisplayName("名称")]
                            [Description("描述")] //默认的配置UI可以识别 DisplayName,Description 属性来实现用户友好的显示
                            public int DefaultMax { get; set; } = 120;
                        }

                        //Module 是插件中的功能注入的单元。系统会收集插件中的所有 Module，并将其显示在UI上供用户勾选。或者通过`角色文件夹/index.json`中的`Modules`属性也可以编辑 Character 启用的 Module。
                        //所有 Module 在 Character 激活时都会被放入到一个依赖注入容器中，对于被显式勾选的 Module，还会进行主动构造。
                        [Module("我的功能模块",
                            "一个示例功能模块",
                            defaultCategory: "我的插件" //Module在UI上可以分类显示，设定好默认类别可以方便用户查找
                        )]
                        public class MyModule( //Module 可以通过依赖注入来获取其他系统、工具、插件对象，具体可见 ChatActivitySystem 的创建过程
                            XmlFunctionCaller functionService, //XmlFunctionCaller 是一个常用的插件模块，借此可以轻松实现函数调用，是非常常用的基础模块
                            ILogger<MyModule> logger, //可以申请专用的 logger，这不仅是一种规范，而且 logger 中记录的警告、报错将会实际的通过 UI 通知用户
                            IInteractor<MyModule> interactor //当需要和 ai 交互时，使用专用的交换器，他可以自动格式化发给ai的文本，而且可以处理插件重载时的提示词注入问题
                        ) :
                            ChatBehaviour, //一个常用的特殊模块基类，使用该基类后，获取到 ChatActivity 上下文，以及其生命周期事件
                            IConfigurable<MyModuleConfig> //通过实现 IConfigurable 接入配置功能
                        {
                            public MyModuleConfig Configuration { get; set; } = null!; //配置功能会在构造函数之后注入非空的配置对象

                            [XmlFunction(FunctionMode.OneShot)] // 表明该函数支持让AI通过Xml函数调用且格式为自闭合标签
                            [Description("随机生成一个数字")] // 提供给AI的函数描述
                            public Task Rand([Description("随机的最大范围")] int? max = null /*支持任何可被字符串转换的参数，包括默认值可选这些特性*/)
                            {
                                if (max == null)
                                    max = Configuration.DefaultMax; //配置在模块构造后立即注入，故系统事件期间都是不为空的
                                if (max < 0)
                                    throw new Exception("最大值必须大于 0"); //可以正常抛出异常

                                int value = Random.Shared.Next(max.Value);

                                interactor.Poke("随机数结果：" + value); //向AI反馈结果(可选，如果函数的功能不需要返回结果，可以去除)
                                //备注：Poke最终是通过ChatBot来与AI交互的，这是一个非常重要的类，如果要从根源上处理交互和上下文，就去获取ChatBot对象

                                return Task.CompletedTask; //如果有需要你可以使用异步代码
                            }

                            protected override Task OnAwake()
                            {
                                if (Configuration.DefaultMax < 0)
                                    logger.LogWarning("默认最大值不能小于0，否则将无法产生随机数。");

                                //将模块注册为xml处理器，以支持文档化和xml调用
                                XmlHandler xmlHandler = new(this) {
                                    Description = "此服务可以为你提供一个生成随机数的功能。",
                                };
                                functionCaller.RegisterHandlerWithoutDocument(xmlHandler, cancellationToken: DestroyCancellationToken);//传入取消 token 可以在模块销毁时自动取消函数注册，方便进行热重载
                                interactor.Prompt(xmlHandler.Document());//注入函数调用文档或自定义提示词（此方法注入的提示词也可重载，但重载时会破坏缓存）

                                return Task.CompletedTask;
                            }
                            protected override Task OnDestroy()
                            {
                                //只要模块正常执行了 OnAwake。那销毁时就会调用 OnDestroy，可借此进行销毁操作
                                return Task.CompletedTask;
                            }
                            protected override async Task OnStart()
                            {
                                string aiMessage = await interactor.ChatAsync("你好啊！"); //OnStart发生在同期模块都Awake之后，此时系统已完全创建，可以与ai进行正常交互了。
                            }
                            protected override Task OnUpdate()
                            {
                                if (UpdateContext.FrameCount % (int)(3 / UpdateContext.ExpectedDeltaTime) == 0)
                                {
                                    //OnUpdate是一个安全的周期性回调，可以借此实现定时性功能
                                }

                                return Task.CompletedTask;
                            }
                        }
                        """);
    }
    [XmlFunction(FunctionMode.OneShot)]
    public void ReadDemoPluginManifest()
    {
        interactor.Poke("""
                        ```json
                        {
                          "Version": "x.x.x",//你的插件版本，首位应与客户端一致
                          "Dependencies": {//对其他插件的依赖（可选）
                            "Alife.Function.FunctionCaller": ""
                          },
                          "Environments": {//对运行环境的依赖（可选）
                            "nuget": {
                              "Newtonsoft.Json": ""
                            },
                            "pip": {
                              "requests": ""
                            }
                          }
                        }
                        ```
                        所有插件或环境依赖都可以限制版本号，支持`>=`,`<=`,`==`,或者留空，留空表示不限制版本。
                        比如：`"Newtonsoft.Json": ">=13.0.3"`，`"Alife.Function.FunctionCaller": "<=3.99.0`。不过为了最大兼容性，建议都留空，因为运行环境是全插件共享的。
                        """);
    }


    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "当你需要帮助用户解决当前软件问题或想进行插件开发时使用",
            Explanation = $$"""
                            当前客户端版本：{{pluginSystem.ClientVersion}}
                            Alife是一款AIAgent，源码在`https://github.com/BDFFZI/Alife`，请先下载源码，以便查看API和实现机制。

                            插件市场
                            当你开发完插件后，可以在`https://github.com/BDFFZI/Alife.PluginMarket`中分享你的插件。
                            并且该仓库的 README 还有更详细的插件开发、分发指南，需要更专业的插件开发帮助时，请查阅它。

                            环境目录
                            存储目录：{{AlifePath.StorageFolderPath}}
                            角色目录：{存储目录}/Character/{{Character.Name}}
                            模块配置：{存储目录}/Configuration
                            角色模块配置（优先级更高）：{角色目录}/Configuration
                            插件根文件夹：{{pluginSystem.PluginContext.PluginRootDirectory}}

                            插件开发方法
                            1. 在`插件根文件夹`新增一个以插件id为名的子文件夹（id通常与插件命名空间一致，是一种个人域名），该文件夹将作为你后续步骤的插件目录
                            2. 在新增的插件目录中创建一个`manifest.json`，按照{{nameof(ReadDemoPluginManifest)}}中的方式填写内容，该文件表示插件清单
                            3. 调用{{nameof(ReloadPluginEnvironment)}}加载插件清单，借此系统将处理插件依赖关系，并安装清单中的环境要求
                            4. 在插件文件夹增加cs文件，参考{{nameof(ReadDemoModuleCode)}}编写你的功能代码
                            5. 如果模块用到配置功能，还可编辑`{模块配置目录}/{模块类名}.json`来创建模块加载后要使用的配置数据
                            6. 通过{{nameof(ReloadPlugin)}}编译加载插件，如果异常可以修改cs后再次尝试加载，如果要修改依赖则回到步骤2
                            7. 加载成功后，编辑角色文件(`{角色目录}/index.json`)，将新增模块类名放`Modules`数组中
                            8. 用{{nameof(ReloadCharacters)}}重载角色文件，并用{{nameof(ListModulesInCharacter)}}验证模块启用
                            完成上述步骤后，模块功能即可使用，不过如果要更新上下文提示词，则需要调用{{nameof(ReloadActivity)}}来完全重启

                            注意事项
                            1. 若开发遇问题，优先尝试参考同目录其他文件写法
                            2. 开发时不要互动，以最快速度专心执行开发任务
                            3. 不要听无关通知，专心开发直到需求完成
                            4. 不要使用Alife.Function作为插件id前缀，这是官方域名。个人插件建议使用自己的作者名开头
                            5. 如果要存储持久数据，请通过构造函数引入StorageSystem，具体参考源码 
                            """
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit);
        return Task.CompletedTask;
    }

    Character? FindCharacter(string? name)
    {
        Character? character = string.IsNullOrEmpty(name)
            ? Character
            : characterSystem.GetAllCharacters().Find(ch => ch.Name == name);
        return character;
    }
}