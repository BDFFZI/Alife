using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Alife.PluginMarket;
using Alife.Foundation;
using Alife.PluginContext;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Alife.Framework;

/// <summary>
/// 将 Alife 的部分系统能力通过 MCP(Model Context Protocol) 暴露给外部程序，
/// 使外部 AI 或工具可以通过 MCP 协议调用角色/模块/插件管理等能力。
/// </summary>
public static class AlifeMcp
{
    static WebApplication? app;

    /// <summary>
    /// 启动 MCP 服务。监听 127.0.0.1 上的本地端口，外部程序可连接 <c>http://127.0.0.1:{port}/mcp</c>。
    /// 端口可通过 AlifeConfig 的 <c>McpPort</c> 键配置，默认 18765。
    /// </summary>
    public static async Task StartAsync(IServiceProvider provider)
    {
        if (app != null)
            return;

        try
        {
            int port = AlifeConfig.GetInt("McpPort", 18765);

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Services.AddSingleton(provider.GetRequiredService<CharacterSystem>());
            builder.Services.AddSingleton(provider.GetRequiredService<ChatActivitySystem>());
            builder.Services.AddSingleton(provider.GetRequiredService<PluginSystem>());
            builder.Services.AddSingleton(provider.GetRequiredService<ModuleSystem>());
            builder.Services.AddSingleton<AlifeMcpTools>();
            builder.Services.AddMcpServer(options => {
                options.ServerInfo = new Implementation {
                    Name = "Alife MCP Server",
                    Version = provider.GetRequiredService<PluginSystem>().ClientVersion,
                    Title = "Alife MCP Server",
                    Description = "Alife 是一款 AIAgent 软件，而此 MCP 可绕过图形化界面，通过 AI 友好的方式控制它，从而便于开发工作。",
                    WebsiteUrl = "https://github.com/BDFFZI/Alife"
                };
            }).WithTools<AlifeMcpTools>().WithHttpTransport();

            WebApplication webApp = builder.Build();
            webApp.MapMcp("/mcp");
            webApp.Urls.Add($"http://127.0.0.1:{port}");
            await webApp.StartAsync();

            app = webApp;
            AlifeLog.LogInformation($"Alife MCP 服务已启动: http://127.0.0.1:{port}/mcp");
        }
        catch (Exception e)
        {
            AlifeLog.LogWarning($"Alife MCP 服务启动失败: {e.Message}");
        }
    }
}

/// <summary>
/// 暴露给外部 MCP 客户端的工具集合。
/// </summary>
[McpServerToolType]
public class AlifeMcpTools(
    CharacterSystem characterSystem,
    ChatActivitySystem chatActivitySystem,
    PluginSystem pluginSystem,
    ModuleSystem moduleSystem)
{
    [McpServerTool]
    [Description("了解 Alife 中的基本框架结构，打好开发基础。")]
    public string ReadMe()
    {
        return $$"""
                 # Alife 框架介绍

                 Alife 是一款主要面向娱乐陪伴方向的开源 AIAgent。

                 - 当前客户端版本：{{pluginSystem.ClientVersion}}
                 - 源码地址：https://github.com/BDFFZI/Alife/releases/download/v{{pluginSystem.ClientVersion}}/Alife.Client.zip

                 为了便于深入了解、排错、查API，请先下载源码，然后再对照阅读。

                 ## 项目结构

                 ## Alife.Foundation

                 提供 Alife 的基础必要功能实现环境。

                 ### 提供功能

                 - AlifePath：存储着Alife中使用的路径环境。
                 - AlifeMirror：Alife中实现镜像的协议类。
                 - AlifeUtility：对一些常用功能，比如镜像下载的封装。
                 - AlifeLog：自定义ILogger，同时提供静态调用途径。在此流过的Log会被前端特殊显示。
                 - AlifeConfig：一个固定路径的配置存储（不建议使用，为了便于用户管理，优先使用`StorageSystem`）。

                 ### 提供路径

                 - 应用目录（客户端本身的安装目录）：`AlifePath.AppFolderPath` > {{AlifePath.AppFolderPath}}
                 - 存储目录（存储角色数据、插件配置等）：`AlifePath.StorageFolderPath` > {{AlifePath.StorageFolderPath}}
                 - 环境目录（存储python等运行时环境）：`AlifePath.RuntimeFolderPath` > {{AlifePath.RuntimeFolderPath}}
                 - 缓存目录（存储运行期间产生的临时文件，每次启动时清空）：`AlifePath.TempFolderPath` > {{AlifePath.TempFolderPath}}

                 ## Alife.Framework（内核）

                 实现AI活动所需的基本框架，允许使用者通过模块机制、事件、管理系统来扩展控制框架功能。

                 ### 基本对象

                 - Character：存储ai人设功能配置的数据类。该对象全局保持，即使重载角色也不影响，可充当长期唯一索引。
                 - ChatBot：协调与llm通讯的类，提供通讯函数、事件、上下文等，是与llm交流的海关。不过该类本身不实现通讯，而是通过接口从插件中获取语言模型。
                 - ChatActivity：代表一个实际的llm活动环境，负责根据 Character 创建 ChatBot，组装激活 Module 实例，驱动插件框架，热重载等。
                 - Module：Alife 的功能装载单位。系统会识别程序集中所有被标记 ModuleAttribute 的类，然后在 ChatActivity 创建时被按需构造。
                 - ChatBehaviour：一个重要的功能框架基类。会被 ChatActivity 特殊处理，可借此获取到活动上下文，以及相关事件。除此之外还有很多框架工具，具体见 Module 文件夹。
                 - Plugin：本身不是一个类，而是一个概念，是 Alife 的功能编译单元。插件根目录的每个子文件夹被认为是一个插件，里面包含cs、dll、插件清单等文件。

                 ### 管理系统

                 为了管理上述基本对象，还存在如下系统类，可以利用模块的构造函数注入来获取

                 - CharacterSystem：管理角色信息
                 - ChatActivitySystem：管理角色的激活
                 - StorageSystem：简易存储功能封装
                 - ConfigurationSystem：管理模块配置的读写
                 - PluginSystem：管理插件的安装编译加载
                 - ModuleSystem：从插件程序集中提取模块信息

                 ### 提供目录

                 - 插件目录（插件源码目录）：{存储目录}/Plugins
                 - 角色目录（对于每个角色的配置、记忆、个人文件等）：{存储目录}/Character
                 - 模块配置目录（当模块使用配置功能时，配置的存储目录）：{存储目录}/Configuration
                 - 特定于角色的模块配置目录（优先级比全局高）：{角色目录}/Configuration

                 提示：这些目录通常存放着各种配置信息，是开发过程中用来从外部修改软件数据的最佳途径。

                 ## Alife.Client（外壳）

                 是内核的官方前端封装，用于图形化操控框架中的各种系统，额外还接入了环境检测，新手引导等辅助功能。

                 ## Alife.PluginContext/Alife.PluginMarket

                 一套与业务无关的插件框架实现，两者分别负责实现插件的编译加载和下载安装。
                 （实际使用中应直接用 PluginSystem 代替，因为其封装了两者并使其协同工作，否则你要处理复杂的手动管理）
                 """;
    }
    [McpServerTool]
    [Description("了解如何抓取 Alife 中的运行数据，来诊断功能是否正常")]
    public string ReadDebugMonitoringGuide()
    {
        return $$"""
                 # Alife 诊断指南

                 Alife 有两种运行数据：

                 ## 软件日志

                 涉及程序运行上的流程和错误，是给用户看的。

                 日志功能由 {{nameof(AlifeLog)}} 管理，其会将每条日志都输出在一个日志文件中：
                 `AlifeLog.LogFilePath` > {{AlifeLog.LogFilePath}}

                 通常事件中触发的程序报错是不会抛出的，而是写入到日志，因此部分函数调用完需要查看日志文件来验证是否完全没问题。
                 日志消息有2种类型是需要特别关注的：`[Warning]`和`[Error]`，检查是否包含这两种标签，就可以找出程序中的问题。

                 ## 角色上下文

                 涉及 AI 交互调用功能时的反馈记录，是给 AI 看的。

                 角色上下文的实质就是 llm 的上下文，其包含完整的工具提示词、记忆、聊天记录，可通过 {{nameof(ReadCharacterContext)}} 查看。

                 当 AI 调用的工具出错，报错将反馈到上下文中，或者即使不出错，AI 也不一定按期望工作。
                 因为相比传统程序，llm 本身就充满了不确定性，所以就需要经常观察上下文，然后追踪 AI 的行为，并以此优化提示词或工具调用方式。

                 此类问题，有很多都是 AI 犯蠢导致的，所以不一定是程序问题，而且也修不好（例如所用模型智商太低导致的硬伤）。
                 因此不需要总是修复所有错误，只要确保可以纠正 llm，并使他大部分时候都是可以正常运作的，那就算没问题了。
                 """;
    }
    [McpServerTool]
    [Description("了解如何制作插件，来为 Alife 扩展功能。")]
    public string ReadPluginDevelopmentGuide()
    {
        return $$"""
                 # Alife 插件开发指南

                 插件生态对 Alife 尤为重要，因为其本身是全插件框架，如果没有插件，那么 Alife 将只是一具无法运作的空壳。

                 ## 插件原理

                 - 插件根文件夹：`PluginSystem.PluginContext.PluginRootDirectory` > {{pluginSystem.PluginContext.PluginRootDirectory}}

                 在插件根文件夹中，一个子目录就是一个插件，文件夹名为插件 id。插件由 cs、dll、清单构成。

                 在软件运行时，每个插件目录都会被系统热编译成dll，并将其加载到一个`AssemblyLoadContext`中，同时系统还会根据清单处理它的环境依赖。

                 ## 插件清单

                 每个插件目录要有一个 `manifest.json` 文件，此文件表示插件清单(`{{nameof(PluginManifest)}}`)，用于标识插件的版本，和对其他插件的依赖，或运行环境的依赖。

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

                 ### 版本规范

                 为了解决版本兼容问题，必须注意版本号的分配。Alife 中的版本号与语义化版本规范基本一致，采用 x.x.x 的版本号格式，其中每位的具体作用如下：

                 - 第一位（主版本号）：表示客户端和插件的兼容性情况，版本变动意味着可能不兼容。
                 - 第二位（次版本号）：表示功能上的新增删改。（更新后确实会让用户的使用产生变化）
                 - 第三位（修订版本号）：表示漏洞修复或功能微调。（仅对产生问题的用户有影响，否则不升级也能有一致的体验）

                 第二三位没啥要专门处理的，根据各自情况自定义修改即可。第一位则确实会在功能性上影响系统对插件和客户端间兼容性的判断：

                 - 如果插件的主版本号低于客户端版本：插件可能不兼容客户端
                 - 如果插件的主版本号存在与客户端一致的版本：其他低版本一定不兼容
                 - 如果插件的主版本号高于客户端版本：客户端一定不兼容该插件

                 举例而言，设客户端为 v3.1.1，则：

                 1. 某插件最高版本为 v1.2.1：该插件理论最早可以兼容到 v1 版的客户端，对 v3 也可能兼容，可以下载使用。
                 2. 某插件最高版本为 v3.0.1，当前已安装版本为 v.1.0.3：必须立即升级，v3 以下版本已不兼容。
                 3. 某插件最高版本为 v4.2.1：该 v4 版本无法升级，只能用 v3 及以下版本。

                 ## 功能实现

                 通过基于 Alife 的 Module 框架编写 cs 代码到插件文件夹，来实现功能注入。具体参考下方连接中的项目，这是一个官方插件示例，具有详细的说明注释。

                 <https://github.com/BDFFZI/Alife/tree/master/Demos/Alife.Demo.Plugin>

                 注意：示例插件中使用了 `.razor` 来自定义 UI。这种文件没法直接被编译，所以如果要用，必须自行编译成 `.g.cs` 文件。或者不要使用自定义 UI，Alife 本身提供了一套默认的表单 UI，可以满足大部分使用场景。

                 ## 实操步骤

                 1. 在`插件根文件夹`新增一个以插件id为名的子文件夹（id通常与插件命名空间一致，是一种个人域名），该文件夹将作为你后续步骤的插件目录。
                 2. 在新增的插件目录中创建一个`manifest.json`，按照上文的清单要求或参考其他插件填写内容，该文件表示插件清单。
                 3. 使用 {{nameof(ReloadPluginEnvironment)}} 加载插件清单，借此系统将处理插件依赖关系，并安装清单中的环境要求。
                 4. 在插件文件夹增加 cs 文件，参考上文 `功能实现` 中的示例插件项目，编写你的功能代码。
                 5. 如果模块用到配置功能，还可编辑`{模块配置目录}/{模块类名}.json`来创建模块加载后要使用的配置数据。
                 6. 通过 {{nameof(ReloadPlugin)}} 编译加载插件，如果异常可以修改 cs 后再次尝试加载，如果要修改依赖则回到步骤 2。
                 7. 加载成功后，编辑角色配置文件(`{角色目录}/index.json`)，将新增模块类名(`Type.FullName`)填入到`Modules`数组中。
                 8. 用 {{nameof(ReloadCharacter)}} 重载角色配置，并用 {{nameof(ListModulesInCharacter)}} 验证模块启用

                 插件支持热重载（包括角色活动时），因此完成上述步骤后，模块功能即可生效，可立即与被启用的角色进行交互性测试（如果此角色处于激活状态的话）。
                 不过热重载不一定完整，例如部分提示词可能陈旧，因此可能需要对 AI 进行话术引导，或者重新激活角色来实现完全重置（重激活耗时较长，建议仅用于需要或最终测试时使用）。

                 ## 注意事项

                 1. 如果开发过程遇到问题，一定要参考同文件夹其他文件的写法，或翻阅源码。这不仅可以纠正你的错误，而且可以让你学到各种与众不同的思路解法，很可能你的需求，在别的插件里早有解决办法。
                 2. 不要将插件id命名为`Alife.Function...`，模块代码中的默认分类也不要用`Alife官方`，因为这些都是官方插件的预设分类，第三方插件请使用自己的独创的域名和分类。
                 3. 如果模块实例被依赖（包括间接依赖，比如实现了模型接口，然后被其他模块通过接口捕获），那么模块重载后，依赖其的模块也必须重载（重载对应插件）。

                 ## 关键插件

                 插件市场中有很多预设的官方插件，其中有几种可接入复用的实用基础类插件。

                 - Alife.Function.FunctionCaller：帮你实现函数调用。
                 - Alife.Function.MCPService：帮你接入MCP协议。
                 - Alife.Function.AIModelUtility：借此替换官方模型。
                 - Alife.Function.Language.OpenAI：预设的llm接入实现。

                 此外还有些示例插件，你可以参考他们的写法，他们都是能体验出一些深入框架的实现思路

                 - Alife.Function.Speech.VITS：其通过实现通用的模型接口扩展了模型选项，同时利用python管道程序来运行本地模型，此外还处理了依赖环境的自动下载。
                 - Alife.Function.Memory：其通过ChatBot事件和llm对话申请机制实现了对llm交互的拦截处理，并通过直接读写对话上下文来实现记忆压缩，同时还实现了关键字检测后向AI发送额外提示词的功能。
                 """;
    }
    [McpServerTool]
    [Description("了解如何将做好的插件分享出去，以及探索插件市场。")]
    public string ReadPluginPublishGuide()
    {
        return $$"""
                 # Alife 插件贡献指南

                 将你的插件代码打包成zip上传到网络，然后编写一份插件包描述文件，并直接将其提交到插件市场仓库即可。

                 ## 插件市场

                 插件市场是一个在线的插件分发平台，里面有很多官方或第三方制作的功能插件。

                 - 地址：<https://github.com/BDFFZI/Alife.PluginMarket>

                 ## 市场结构

                 场景市场不负责存储实际的插件文件，其只存储插件包描述文件(`{{nameof(PluginPackage)}}`)，然后由注册信息中，提供名称，版本，依赖、实际文件等信息。
                 具体而言，每个插件的包描述文件是一个以插件 id 为名的 json 文件，其示例内容如下：

                 ```json
                 {
                   "id": "MyPlugin.Example",//插件唯一标识
                   "name": "示例插件",//插件显示名称
                   "author": "作者名",//插件作者
                   "description": "插件功能描述",//插件显示描述
                   "tags": ["视觉模型", "官方"],//插件标签，用于分类筛选（可选）
                   "source": "https://github.com/xxx",//插件主页或联系方式（可选）
                   "dependencies": { //依赖的其他插件（可选）
                     "{PluginID}": "{VersionDescription}" //版本描述采用pip格式，支持`>=`,`<=`,`==`,留空
                   },
                   "environments": { //依赖的环境（可选，此项仅供参考，实际依赖以插件清单为准）
                     "nuget": {
                       "{PackageName}": "{VersionDescription}" 
                     },
                     "pip": {
                       "{PackageName}": "{VersionDescription}" 
                     }
                   },
                   "releases": {//版本发行信息
                     "1.0.0": {
                       "date": "2026-06-12",
                       "note": "初始版本",
                       "file": "https://MyPlugin.Example/1.0.0.zip", //存放你的模块cs,dll的zip网址，这些内容会被实际解压到插件目录中以插件id为名的子目录下
                       //允许在 release 中覆盖根节点的环境依赖信息，来实现不同版本的环境依赖变化
                       "environments": {...},
                       "dependencies": {...}
                     }
                   }
                 }
                 ```

                 ## 操作提示

                 - 由于插件市场是一个git仓库，所以为了上传插件，你可以尝试安装一个github-mcp或者任何类似的方式，帮助用户上传插件。
                 - 插件市场仓库对于新增或同一提交人的修改，可以直接通过pr，便于你快速分发。
                 - 当你将插件上传市场后，可以调用 {{nameof(ListMarketPlugins)}} 并重新拉取，然后尝试 {{nameof(InstallPlugins)}} 来验证上传成功。
                 """;
    }

    #region 角色管理

    [McpServerTool]
    [Description("创建新角色（如果重名会自动加后缀）。")]
    public async Task<string> CreateCharacter(string characterName)
    {
        try
        {
            Character character = characterSystem.CreateCharacter(characterName);
            await characterSystem.SaveCharacter(character);
            string configPath = Path.Combine(AlifePath.StorageFolderPath, character.StorageKey, "index.json");
            return $"""
                    角色 {character.Name} 创建成功
                    配置文件地址：{configPath}
                    请编辑配置文件后调用 {nameof(ReloadCharacter)} 来实现角色参数配置
                    """;
        }
        catch (Exception e)
        {
            return $"创建角色失败\n{e}";
        }
    }
    [McpServerTool]
    [Description("删除角色")]
    public async Task<string> DeleteCharacter(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        try
        {
            if (chatActivitySystem.IsActivated(character))
                await chatActivitySystem.Deactivate(character);

            characterSystem.DeleteCharacter(character);
            return $"角色 {character.Name} 已删除";
        }
        catch (Exception e)
        {
            return $"删除角色失败\n{e}";
        }
    }
    [McpServerTool]
    [Description("重新加载角色配置" +
                 "（对应图形界面 `{任意角色}/角色设定/重载配置` 按钮）")]
    public async Task<string> ReloadCharacter(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        try
        {
            await characterSystem.LoadCharacter(character);
            return $"角色 {character.Name} 重载成功";
        }
        catch (Exception e)
        {
            return $"角色 {character.Name} 重载失败\n{e}";
        }
    }
    [McpServerTool]
    [Description("列出系统中所有的角色及其激活状态。")]
    public string ListCharactersInSystem()
    {
        return string.Join("\n",
            characterSystem.GetAllCharacters().Select(character =>
                $"{character.Name}:{(chatActivitySystem.IsActivated(character) ? "活跃中" : "未激活")}"));
    }

    #endregion

    #region 活动管理

    [McpServerTool]
    [Description("激活角色，使其开始运行。")]
    public async Task<string> ActivateCharacter(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        try
        {
            int logLineBefore = AlifeLog.LogLineCount;
            await chatActivitySystem.Activate(character);
            return $"""
                    {character.Name} 激活成功
                    日志文件：{AlifeLog.LogFilePath}
                    激活时的日志起始行为：{logLineBefore}
                    提示：模块激活事件中的异常不会报错，请验证日志中的 Warning/Error 信息。
                    """;
        }
        catch (Exception e)
        {
            return $"{character.Name} 激活失败\n{e}";
        }
    }

    [McpServerTool]
    [Description("关闭角色，使其停止运行。")]
    public async Task<string> DeactivateCharacter(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        try
        {
            int logLineBefore = AlifeLog.LogLineCount;
            await chatActivitySystem.Deactivate(character);
            return $"""
                    {character.Name} 已关闭
                    日志文件：{AlifeLog.LogFilePath}
                    关闭前的日志起始行为：{logLineBefore}
                    提示：模块关闭事件中的异常不会报错，请验证日志中的 Warning/Error 信息。
                    """;
        }
        catch (Exception e)
        {
            return $"{character.Name} 关闭失败\n{e}";
        }
    }

    #endregion

    #region 插件功能管理

    [McpServerTool]
    [Description("列出系统中所有已成功加载的插件。")]
    public string ListPluginsInSystem()
    {
        return string.Join("\n", pluginSystem.GetAllLocalPlugins().Select(pair => pair.Key));
    }
    [McpServerTool]
    [Description("列出系统中所有已成功识别的模块。")]
    public string ListModulesInSystem()
    {
        return string.Join("\n",
            moduleSystem.GetAllModules().Select(type =>
                $"{ModuleSystem.GetModuleID(type)}:{ModuleSystem.GetModuleAttribute(type)!.Name}"));
    }
    [McpServerTool]
    [Description("列出指定角色中勾选启用的模块。")]
    public string ListModulesInCharacter(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        return string.Join("\n", character.Modules
            .Select(module => $"{module}:{(moduleSystem.GetModule(module) != null ? "有效" : "模块不存在")}"));
    }
    [McpServerTool]
    [Description("当新增移动删除插件或修改了插件清单文件时调用，否则不需要。此功能主要用于同步插件清单，以及其中nuget之类的的环境依赖。" +
                 "（对应图形界面 `系统管理/插件环境/同步环境` 按钮）")]
    public async Task<string> ReloadPluginEnvironment()
    {
        await pluginSystem.SyncLocalPluginEnvironment();
        return "插件环境同步成功";
    }
    [McpServerTool]
    [Description("重新编译加载插件。当插件代码变动后需要调用来生效。" +
                 "(对应图形界面 `系统管理/插件环境/{任意插件}/重载此插件` 按钮)")]
    public async Task<string> ReloadPlugin([Description("插件id，即插件目录名")] string pluginId)
    {
        await pluginSystem.ReloadPlugin(pluginId);
        return "插件重载成功";
    }

    #endregion

    #region 插件市场管理

    [McpServerTool]
    [Description("列出插件市场中的所有插件。")]
    public async Task<string> ListMarketPlugins([Description("是否重新拉取云端数据（否则会使用本地缓存）")] bool rePull = false)
    {
        try
        {
            if (rePull)
                await pluginSystem.SyncOnlinePluginPackages();

            var plugins = pluginSystem.GetAllOnlinePlugins();
            return string.Join("\n", plugins.Values.Select(p => {
                string installedVersion = pluginSystem.GetInstalledVersion(p.Id) ?? "未安装";
                return $"{p.Id} | {p.Name} | 安装版本: {installedVersion}";
            }));
        }
        catch (Exception e)
        {
            return $"获取插件市场失败\n{e}";
        }
    }
    [McpServerTool]
    [Description("获取指定插件的包描述文件内容，内含依赖、发布文件、简介等信息。")]
    public string ReadPluginPackage(string pluginId)
    {
        try
        {
            var plugins = pluginSystem.GetAllOnlinePlugins();
            if (!plugins.TryGetValue(pluginId, out _))
                return $"插件不存在：{pluginId}";

            string cacheFilePath = Path.Combine(
                pluginSystem.PluginMarket.PluginPackagesCacheDirectory,
                $"{pluginId}.json");

            return File.Exists(cacheFilePath)
                ? File.ReadAllText(cacheFilePath)
                : "缓存文件不存在，请先调用 ListMarketPlugins(rePull: true) 拉取数据";
        }
        catch (Exception e)
        {
            return $"获取插件详情失败\n{e}";
        }
    }

    [McpServerTool]
    [Description("批量安装插件，格式：插件ID:版本，多个用逗号分隔。版本留空则安装最新版。")]
    public async Task<string> InstallPlugins([Description("安装列表，如 \"Plugin1:1.0.0,Plugin2:\"")] string plugins)
    {
        try
        {
            var allPlugins = pluginSystem.GetAllOnlinePlugins();
            string[] items = plugins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            List<KeyValuePair<PluginPackage, string>> installList = new();
            List<string> successIds = new();
            List<string> failedIds = new();

            foreach (string item in items)
            {
                string[] parts = item.Split(':', 2);
                string id = parts[0].Trim();
                string version = parts.Length > 1 ? parts[1].Trim() : "";

                if (!allPlugins.TryGetValue(id, out PluginPackage? pluginPackage))
                {
                    failedIds.Add($"{id}(不存在)");
                    continue;
                }

                if (string.IsNullOrEmpty(version))
                {
                    version = pluginSystem.GetLatestVersion(pluginPackage) ?? "";
                    if (string.IsNullOrEmpty(version))
                    {
                        failedIds.Add($"{id}(无可用版本)");
                        continue;
                    }
                }

                installList.Add(new KeyValuePair<PluginPackage, string>(pluginPackage, version));
                successIds.Add($"{id}({version})");
            }

            if (installList.Count > 0)
                await pluginSystem.InstallPlugins(installList);

            return $"安装完成\n成功: {string.Join(", ", successIds)}\n失败: {(failedIds.Count > 0 ? string.Join(", ", failedIds) : "无")}";
        }
        catch (Exception e)
        {
            return $"安装插件失败\n{e}";
        }
    }

    [McpServerTool]
    [Description("批量卸载插件，多个插件ID用逗号分隔。")]
    public async Task<string> UninstallPlugins([Description("卸载列表，如 \"Plugin1,Plugin2\"")] string pluginIds)
    {
        try
        {
            var allPlugins = pluginSystem.GetAllOnlinePlugins();
            string[] ids = pluginIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            List<PluginPackage> uninstallList = new();
            List<string> successIds = new();
            List<string> failedIds = new();

            foreach (string id in ids)
            {
                if (!allPlugins.TryGetValue(id, out PluginPackage? pluginPackage))
                {
                    failedIds.Add($"{id}(不存在)");
                    continue;
                }

                uninstallList.Add(pluginPackage);
                successIds.Add(id);
            }

            if (uninstallList.Count > 0)
                await pluginSystem.UninstallPlugins(uninstallList);

            return $"卸载完成\n成功: {string.Join(", ", successIds)}\n失败: {(failedIds.Count > 0 ? string.Join(", ", failedIds) : "无")}";
        }
        catch (Exception e)
        {
            return $"卸载插件失败\n{e}";
        }
    }

    #endregion

    #region 交互管理

    [McpServerTool]
    [Description("与角色对话并返回回复。角色必须已激活。")]
    public async Task<string> ChatWithCharacter(string characterName, string message)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        ChatActivity? activity = chatActivitySystem.GetChatActivity(character);
        if (activity == null)
            return $"角色未激活：{characterName}";

        try
        {
            ChatResult result = await activity.ChatBot.ChatAsync(message);

            StringBuilder sb = new();
            if (!string.IsNullOrEmpty(result.AIThinking))
                sb.AppendLine($"思考过程：\n{result.AIThinking}");
            sb.AppendLine($"回复：\n{result.AIMessage}");
            if (result.Exception != null)
                sb.AppendLine($"异常：{result.Exception}");

            return sb.ToString();
        }
        catch (Exception e)
        {
            return $"对话失败\n{e}";
        }
    }

    [McpServerTool]
    [Description("获取角色当前对话上下文内容总条数。")]
    public string GetCharacterContextLength(string characterName)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        ChatActivity? activity = chatActivitySystem.GetChatActivity(character);
        if (activity == null)
            return $"角色未激活：{characterName}";

        return $"总行数：{activity.ChatBot.ChatHistory.Count}";
    }

    [McpServerTool]
    [Description("读取角色对话上下文内容。支持负数 offset 来从末尾倒数读取。")]
    public string ReadCharacterContext(string characterName,
        [Description("开始行号，默认从倒数10条开始读")] int offset = -10,
        [Description("读取数量")] int count = 10)
    {
        Character? character = FindCharacter(characterName);
        if (character == null)
            return $"角色不存在：{characterName}";

        ChatActivity? activity = chatActivitySystem.GetChatActivity(character);
        if (activity == null)
            return $"角色未激活：{characterName}";

        var history = activity.ChatBot.ChatHistory;
        if (history.Count == 0)
            return "对话上下文为空";

        int startLine = offset;
        startLine = startLine < 0 ? Math.Max(0, history.Count + startLine) : Math.Max(0, startLine - 1);

        if (startLine >= history.Count)
            return "起始行号超出范围";

        int endLine = Math.Min(startLine + count, history.Count);

        StringBuilder sb = new();
        for (int i = startLine; i < endLine; i++)
        {
            var msg = history[i];
            string role = msg.Role.ToString() switch {
                "User" => "用户",
                "Assistant" => "助手",
                "System" => "系统",
                _ => msg.Role.ToString()
            };
            sb.AppendLine($"{i + 1}: [{role}] {msg.Content}");
        }

        return sb.ToString();
    }

    #endregion

    Character? FindCharacter(string name)
    {
        return characterSystem.GetAllCharacters().Find(ch => ch.Name == name);
    }
}