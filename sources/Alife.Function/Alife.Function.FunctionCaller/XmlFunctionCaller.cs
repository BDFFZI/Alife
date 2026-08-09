using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Function.FunctionCaller;

public class XmlFunctionCallerConfig
{
    [Description("触发子句分隔的字符标记。调整子句会对字幕、语音生成等流式输出的功能产生影响")]
    public List<string> Separators { get; set; } = ["，", "。", "！", "？", "......", "~", "…"];

    [Description("触发子句分隔的最短文本长度（字符数）")]
    public int MinBreakingLength { get; set; } = 23;

    [DisplayName("自动思考")]
    [Description("根据是否使用隐式功能或报错情况，自动开启思考。")]
    public bool AutoThinking { get; set; } = true;
}

public enum DocumentMode
{
    None,
    Implicit,
    Explicit,
}

public partial class XmlFunctionCaller
{
    public static string GetDocumentTag(XmlHandler handler)
    {
        return $"[使用文档({handler.Name})]";
    }
    public static bool HasDocumentTag(string message)
    {
        return message.Contains("[使用文档(");
    }
}

[Module(
    "Xml函数执行器",
    "提供一种Xml函数调用框架，可以将注册其中的函数，暴露给AI，并指导其用Xml标签调用。",
    launchOrder: -10000, //在活动开始之前，将收集到的函数调用信息注入
    defaultCategory: "Alife 官方/功能底座")]
public partial class XmlFunctionCaller(
    ILogger<XmlFunctionCaller> logger,
    IInteractor<XmlFunctionCaller> interactor) :
    ChatBehaviour,
    IConfigurable<XmlFunctionCallerConfig>
{
    public event Func<Task>? ChatCalledAsync;
    public XmlFunctionCallerConfig Configuration { get; set; } = null!;
    public bool IsIdle => executor.IsInactive;
    /// <summary>
    /// 当前系统中的函数调用注册信息。
    /// XmlHandlerTable支持你禁用其中的部分函数，从而实现拦截或手动调用的需求
    /// </summary>
    public XmlHandlerTable HandlerTable => handlerTable;

    public void RegisterHandler(XmlHandler handler, DocumentMode documentMode = DocumentMode.Explicit, CancellationToken cancellationToken = default)
    {
        handlerTable.Register(handler);
        switch (documentMode)
        {
            case DocumentMode.None:
                break;
            case DocumentMode.Implicit:
                if (handler.Name == null)
                    throw new Exception("不支持没有名称的隐式 XmlHandler");
                implicitHandlers.Add(handler);
                AddImplicitTrigger(handler);
                break;
            case DocumentMode.Explicit:
                explicitHandlers.Add(handler);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(documentMode), documentMode, null);
        }
        if (cancellationToken != CancellationToken.None)
            cancellationToken.Register(() => UnregisterHandler(handler));

        UpdatePrompt();
    }
    public void RegisterHandlerWithoutDocument(XmlHandler handler, CancellationToken cancellationToken = default)
    {
        RegisterHandler(handler, DocumentMode.None, cancellationToken);
    }
    public void RegisterHandler(object handler, CancellationToken cancellationToken = default)
    {
        RegisterHandler(new XmlHandler(handler), cancellationToken: cancellationToken);
    }
    public void UnregisterHandler(XmlHandler handler)
    {
        handlerTable.Unregister(handler);
        explicitHandlers.Remove(handler);
        implicitHandlers.Remove(handler);

        UpdatePrompt();
    }
    /// <summary>
    /// 标记指定的xml标签内容均为生文本，不要参与xml解析，这意味着ai不需要考虑这些xml标签内容的通配问题。
    /// xml函数中被标记为[XmlForm]的参数会自动注册为plainArea
    /// </summary>
    /// <param name="plainAreas"></param>
    public void AddPlainAreas(params IEnumerable<string> plainAreas)
    {
        foreach (var plainArea in plainAreas)
            this.plainAreas.Add(plainArea.ToLower());
    }

    readonly XmlHandlerTable handlerTable = new();
    readonly List<XmlHandler> explicitHandlers = new();
    readonly List<XmlHandler> implicitHandlers = new();
    readonly HashSet<string> plainAreas = new();
    XmlStreamParser parser = null!;
    XmlStreamExecutor executor = null!;
    OccupationMarker? chatOccupationMarker;
    //自动思考功能
    IThinkingAbility? thinkingAbility;
    OccupationMarker? thinkingOccupationMarker;
    readonly List<string> thinkingReasons = new();

    protected override Task OnAwake()
    {
        UpdatePrompt(); //提前注入一个提示词块
        return Task.CompletedTask;
    }
    protected override Task OnStart()
    {
        thinkingAbility = ChatBot.LanguageModel as IThinkingAbility;

        //统计XmlForm参数以便注册为纯文本区域
        IEnumerable<XmlParameter> parameters = handlerTable.GetAllHandlers()
            .SelectMany(handler => handler.Functions
                .SelectMany(function => function.Parameters));
        AddPlainAreas(parameters.Where(parameter => parameter.IsXmlForm)
            .Select(parameter => parameter.Name));

        //创建xml解析执行器等
        parser = new XmlStreamParser(plainAreas);
        executor = new XmlStreamExecutor(
            parser,
            handlerTable,
            Configuration.Separators.ToArray(),
            minBreakingLength: Configuration.MinBreakingLength
        );
        parser.Error += OnError;
        executor.Error += OnError;
        executor.Handling += OnHandling;

        //AI输入回调
        ChatBot.ChatSent += OnChatSent;
        ChatBot.ChatReceived += OnChatReceived;
        ChatBot.ChatFinishedAsync += OnChatFinishedAsync;

        return Task.CompletedTask;
    }
    protected override async Task OnDestroy()
    {
        await executor.CancelAndClearAsync();
        await executor.DisposeAsync();
    }

    void OnChatSent(string obj)
    {
        chatOccupationMarker = ChatBot.ResourceOccupiedReason.Rent("函数执行");
        thinkingReasons.Clear();
    }
    void OnChatReceived(string obj)
    {
        executor.Feed(obj);
    }
    void OnHandling(string name, XmlContext context)
    {
        chatOccupationMarker!.Reason = $"执行{name}函数";

        if (context.CallMode != CallMode.Opening && context.CallMode != CallMode.OneShot)
            return;

        //实现当ai调用隐射函数时自动注入对应的隐式文档
        IReadOnlyList<XmlHandler>? handlers = handlerTable.GetHandlersOfFunction(name);
        if (handlers != null) //寻找当前函数的调用处理器
        {
            foreach (XmlHandler handler in handlers)
            {
                if (implicitHandlers.Contains(handler))
                {
                    string documentTag = GetDocumentTag(handler);
                    bool hasDocumentTag = ChatBot.ChatHistory
                        .Where(content => content.Role == AuthorRole.User)
                        .Any(content => content.Content?.Contains(documentTag) ?? false);

                    if (hasDocumentTag == false)
                    {
                        interactor.Poke(GetExplicitDocument(handler));
                        thinkingReasons.Add("重新激活隐式功能");
                    }
                }
            }
        }
    }
    void OnError(string tag, Exception exception)
    {
        interactor.Poke($"执行{tag}标签出错：{exception.Message}");
        logger.LogInformation(exception, $"执行{tag}标签出错");
        thinkingReasons.Add("需要处理函数异常");
    }
    async Task OnChatFinishedAsync(ChatContext chatContext)
    {
        //等待函数执行完成
        {
            try
            {
                await executor.WaitToInactive(chatContext.CancellationToken);
                executor.Flush(); //清理缓冲区，内部可能带有残留数据
            }
            catch (OperationCanceledException)
            {
                //对话被打断，取消执行
                await executor.CancelAndClearAsync();
            }

            ChatBot.ResourceOccupiedReason.Return(chatOccupationMarker!);

            if (ChatCalledAsync != null)
            {
                try
                {
                    await Task.WhenAll(ChatCalledAsync.GetInvocationList()
                        .Cast<Func<Task>>()
                        .Select(func => func()));
                }
                catch (Exception e)
                {
                    AlifeLog.LogError(e);
                }
            }
        }

        if (ChatBot.ChatHistory
            .Where(content => content.Role == AuthorRole.User)
            .Any(content => content.Content != null && HasDocumentTag(content.Content)))
            thinkingReasons.Add("隐式功能激活中");

        //使用自动思考功能
        if (Configuration.AutoThinking && thinkingAbility != null)
        {
            if (thinkingReasons.Count != 0)
            {
                if (thinkingOccupationMarker == null)
                    thinkingOccupationMarker = thinkingAbility.ThinkingRequest.Rent(string.Join(" | ", thinkingReasons));
                else
                    thinkingOccupationMarker.Reason = string.Join(" | ", thinkingReasons);
            }
            else if (thinkingOccupationMarker != null)
            {
                thinkingAbility.ThinkingRequest.Return(thinkingOccupationMarker);
                thinkingOccupationMarker = null;
            }
        }
    }

    string GetExplicitDocument(XmlHandler handler)
    {
        return $"""
                {GetDocumentTag(handler)}
                {handler.Description} 
                #### 提供函数
                {handler.FunctionDocument()}
                {(string.IsNullOrEmpty(handler.Explanation) ? "" : $"#### 详细说明\n```\n{handler.Explanation}\n```\n")}
                """;
    }
    string GetImplicitDocument(XmlHandler handler)
    {
        return $"""
                - <{handler.Name}/> : {handler.Description}
                """;
    }
    void AddImplicitTrigger(XmlHandler source)
    {
        XmlHandler xmlHandler = new(source.Name + "_Trigger");
        xmlHandler.Functions.Add(new XmlFunction {
            Name = source.Name.ToLower(),
            Invoker = (_, _) => {
                interactor.Poke(GetExplicitDocument(source));
                thinkingReasons.Add("即将使用隐式功能");
                return Task.CompletedTask;
            }
        });
        handlerTable.Register(xmlHandler);
    }
    void UpdatePrompt()
    {
        //注入函数文档
        interactor.Prompt(
            $"""
             默认情况下你仅支持输出普通文本，但由于各种插件功能的存在，使得你还拥有通过输出特定的xml标签执行功能调用的能力。

             ## 使用提示
             1. 由于xml的解释器的存在，【" | & | < | >】之类的xml符号都无法直接输出，你需要使用xml转义的方式【&quot; | &amp; | &lt; | &gt;】来输出尖括号。
             2. xml调用方式非常自由，允许你进行嵌套，或一次使用多条。
             3. 很多xml函数拥有调用后返回结果的功能，因此你可以通过多轮对话解决事情（如先调用一下获取手册，然后等到收到结果后，再决定下一步的操作）

             ## 使用示例
             当你的函数足够丰富后，你可以尝试用如下的方式使用他们，这是官方最佳示例（注意，示例中的函数不一定存在）：
             ```
             (可选，未被标签包裹的文字，用户看不到，所以可以在此实现空消息、自言自语、思考等动作)
             <speak> <!-- 默认采用语音方式对外输出，并在文本中穿插表情动作，来实现动态的交互效果 -->
             主人你看我画的好不好看，<expression option="开心" />今天特意给你画的噢！<motion option="摆摆手"/>
             看你每天那么累，给你打打气。
             </speak>
             <python> <!-- 因为python执行需要时间，在结尾调用比较合适。 -->
             show('cheer.png')
             <python>
             ```   

             ## 原始字符串区域
             被如下标签包括的内容可以不用转义，他们会自动保持原始格式
             {string.Join(',', plainAreas)}

             ## 当前可用功能

             {string.Join("\n", explicitHandlers.Select(GetExplicitDocument))}
             """ + (implicitHandlers.Count == 0
                ? ""
                : $"""
                   ## 隐式功能
                   有些功能是渐进式加载的，你需要显式阅读他们文档，来学习如何使用。读取隐式服务的文档非常简单，直接输出xml来调用如下标签即可：

                   {string.Join("\n", implicitHandlers.Select(GetImplicitDocument))}

                   上面这些标签都是开启隐式服务的入口，你要根据实际情况，积极的去调用他们，有很多你需要的功能可能就藏在其中。
                   """)
        );
    }
}