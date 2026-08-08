using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.MessageFilter;

public class MessageReplyRule
{
    public bool Enabled { get; set; } = true;
    public string InputRegex { get; set; } = "";
    public string OutputRegex { get; set; } = "";
    public string CorrectionMessage { get; set; } = "";
}

public class MessageFilterData
{
    public bool EnableTimestamp { get; set; } = true;
    public string MessageAppend { get; set; } =
        "(注意！看清消息来源和意图，不同场合用不同的标签，不要混用；有文档的一定要先查文档，学习标签用法后再进行回复；调用工具时不要编造结果，调完立即停下等待结果返回，然后再进行下一步；回复时要保持发言简洁，禁用旁白、emoji，但允许也建议多用系统支持的图片动作表情等)";
    public int InjectionInterval { get; set; } = 7;
    public string PokeAppend { get; set; } = "";
    public int MaxMessageLength { get; set; } = 10000;
    public List<MessageReplyRule> MessageReplyRules { get; set; } = [
        new() {
            Enabled = true,
            InputRegex = @":\[QChatService\]",
            OutputRegex = @"<q(?:chat|image)",
            CorrectionMessage = "[QChatService]消息必须用<qchat>回复。如果不想发送消息，也请发送空标签。"
        },
        new() {
            Enabled = true,
            InputRegex = @":\[AuditoryService\]",
            OutputRegex = @"<speak",
            CorrectionMessage = "[AuditoryService]消息必须用<speak>标签回复。如果不想发送消息，也请发送空标签。"
        },

        new() {
            Enabled = true,
            InputRegex = @":\[DeskPetService\]",
            OutputRegex = @"<speak",
            CorrectionMessage = "[DeskPetService]消息必须用<speak>标签回复。如果不想发送消息，也请发送空标签。"
        }
    ];
}

[Module("消息过滤",
    "提供添加时间戳、通用提示词、消息格式诊断、最大字长截断等功能。是优化保护AI回复效果的必要插件。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = 10000, //在后期创建，以获得靠后的事件注册顺序
    EditorUI = typeof(MessageFilterServiceUI))]
public class MessageFilterService(
    IInteractor<MessageFilterService> interactor) :
    ChatBehaviour,
    IConfigurable<MessageFilterData>
{
    public MessageFilterData Configuration { get; set; } = null!;

    int injectionCountdown;
    IThinkingAbility? thinkingAbility;
    OccupationMarker? thinkingOccupationMarker;

    protected override Task OnAwake()
    {
        thinkingAbility = ChatBot.LanguageModel as IThinkingAbility;

        ChatBot.ChatSend += OnChatSend;
        ChatBot.PokeSend += OnPokeSend;
        ChatBot.ChatFinished += OnChatFinished;

        interactor.Prompt("在你每次收到的消息中，通常结构如下`[xx]xx(xx)`。其中`[]`表示消息属性，比如记载了发送时间，消息来源等；`()`则是对回复消息时的要求；中间的则是消息正文。注意观察消息属性和附加要求，仔细斟酌后再以正确合适的方式回复。");

        return Task.CompletedTask;
    }

    void OnChatFinished(ChatContext chatContext)
    {
        if (string.IsNullOrEmpty(chatContext.AIMessage))
            return;

        bool needThinking = false;

        foreach (var rule in Configuration.MessageReplyRules)
        {
            if (!rule.Enabled) continue;
            if (string.IsNullOrEmpty(rule.InputRegex)) continue;
            if (string.IsNullOrEmpty(rule.OutputRegex)) continue;
            if (string.IsNullOrEmpty(rule.CorrectionMessage)) continue;

            if (!Regex.IsMatch(chatContext.UserMessage, rule.InputRegex, RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(chatContext.AIMessage, rule.OutputRegex, RegexOptions.IgnoreCase)) continue;

            interactor.Poke(rule.CorrectionMessage);
            needThinking = true;
        }

        if (thinkingAbility != null)
        {
            if (needThinking && thinkingOccupationMarker == null)
            {
                thinkingOccupationMarker = thinkingAbility.ThinkingRequest.Rent("消息回复格式出错");
            }
            else if (thinkingOccupationMarker != null)
            {
                thinkingAbility.ThinkingRequest.Return(thinkingOccupationMarker);
                thinkingOccupationMarker = null;
            }
        }
    }

    string OnChatSend(string message)
    {
        if (message.Length > Configuration.MaxMessageLength)
        {
            message = message.Substring(0, Configuration.MaxMessageLength);
            message += $"(文本过长，超过 {Configuration.MaxMessageLength} 的部分已截断，请注意调整信息读取方式)";
        }

        if (Configuration.EnableTimestamp)
            message = $"当前时间:[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{message}";

        if (injectionCountdown <= 0)
        {
            message = $"{message}\n{Configuration.MessageAppend}";
            injectionCountdown = Configuration.InjectionInterval;
        }
        else
        {
            injectionCountdown--;
        }

        return message;
    }

    string OnPokeSend(string message)
    {
        return $"{message}\n{Configuration.PokeAppend}";
    }
}