using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.MessageFilter;

public class MessageReplyRule
{
    public required string Name { get; init; }
    public required Func<string, bool> InputMatching { get; init; }
    public required Func<string, bool> OutputMatching { get; init; }
    public required Func<string> CorrectionMessage { get; init; }
}

public class RegexMessageReplyRule
{
    public string Name { get; set; } = "";
    public string InputRegex { get; set; } = "";
    public string OutputRegex { get; set; } = "";
    public string CorrectionMessage { get; set; } = "";
}

public partial class MessageFilterService
{
    public static string GetReplyCorrectionTag()
    {
        return "[回复格式纠正]";
    }
}

[Module("消息过滤",
    "提供添加时间戳、通用提示词、消息格式诊断、最大字长截断等功能。是优化保护AI回复效果的必要插件。",
    defaultCategory: "Alife 官方/生活环境",
    LaunchOrder = 10000, //在后期创建，以获得靠后的事件注册顺序
    EditorUI = typeof(MessageFilterServiceUI))]
public partial class MessageFilterService(
    Interactor<MessageFilterService> interactor) :
    ChatBehaviour,
    IConfigurable<MessageFilterServiceConfig>
{
    public MessageFilterServiceConfig Configuration { get; set; } = null!;
    public IReadOnlyList<MessageReplyRule> MessageReplyRules => messageReplyRules;

    public void AddMessageReplyRule(MessageReplyRule messageReplyRule, CancellationToken cancellationToken = default)
    {
        messageReplyRules.Add(messageReplyRule);
        if (cancellationToken != CancellationToken.None)
            cancellationToken.Register(() => UnregisterReplyRule(messageReplyRule));

        void UnregisterReplyRule(MessageReplyRule messageReplyRule)
        {
            messageReplyRules.Remove(messageReplyRule);
        }
    }
    public void AddMessageReplyRule(RegexMessageReplyRule regexMessageReplyRule, CancellationToken cancellationToken = default)
    {
        MessageReplyRule messageReplyRule = new MessageReplyRule() {
            Name = regexMessageReplyRule.Name,
            InputMatching = input => Regex.IsMatch(input, regexMessageReplyRule.InputRegex, RegexOptions.IgnoreCase),
            OutputMatching = output => Regex.IsMatch(output, regexMessageReplyRule.OutputRegex, RegexOptions.IgnoreCase),
            CorrectionMessage = () => regexMessageReplyRule.CorrectionMessage
        };
        AddMessageReplyRule(messageReplyRule, cancellationToken);
    }

    int injectionCountdown;
    OccupationMarker? thinkingOccupationMarker;
    readonly List<MessageReplyRule> messageReplyRules = new();

    protected override Task OnAwake()
    {
        ChatBot.ChatSend += OnChatSend;
        ChatBot.PokeSend += OnPokeSend;
        ChatBot.ChatFinished += OnChatFinished;

        foreach (RegexMessageReplyRuleConfig regexMessageReplyRule in Configuration.MessageReplyRules)
        {
            if (regexMessageReplyRule.Enabled == false)
                continue;
            AddMessageReplyRule(regexMessageReplyRule);
        }
        AddMessageReplyRule(new RegexMessageReplyRuleConfig() {
            Enabled = true,
            InputRegex = ".*",
            OutputRegex = "<.*>",
            CorrectionMessage = "检测到你未使用任何函数调用，如果是遗忘了，请补上。否则请输出 <!----> 表示放弃。"
        });

        interactor.Prompt("在你每次收到的消息中，通常结构如下`[xx]xx(xx)`。其中`[]`表示消息属性，比如记载了发送时间，消息来源等；`()`则是对回复消息时的要求；中间的则是消息正文。注意观察消息属性和附加要求，仔细斟酌后再以正确合适的方式回复。");

        return Task.CompletedTask;
    }

    void OnChatFinished(ChatContext chatContext)
    {
        if (string.IsNullOrEmpty(chatContext.AIMessage))
            return;
        if (chatContext.UserMessage.Contains(GetReplyCorrectionTag()))
            return;

        bool needThinking = false;

        foreach (var rule in messageReplyRules)
        {
            if (rule.InputMatching(chatContext.UserMessage) == false) continue; //非约束消息
            if (rule.OutputMatching(chatContext.AIMessage)) continue; //符合约束条件

            interactor.Poke(GetReplyCorrectionTag() + rule.CorrectionMessage());
            needThinking = true;
        }

        if (needThinking && thinkingOccupationMarker == null)
        {
            thinkingOccupationMarker = ChatBot.LanguageModel.GetThinkingRequester().Rent("消息回复格式出错");
        }
        else if (thinkingOccupationMarker != null)
        {
            ChatBot.LanguageModel.GetThinkingRequester().Return(thinkingOccupationMarker);
            thinkingOccupationMarker = null;
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