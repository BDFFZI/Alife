using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;

// 别名解决冲突，避免使用重名的类作为别名
using PluginBase = global::Alife.Abstractions.Plugin;
using PluginAttr = global::PluginAttribute;
using DialogContext = global::Alife.Modules.Context.DialogContext;
using DialogItem = global::Alife.Modules.Context.DialogItem;
using PythonService = global::Alife.OfficialPlugins.PythonService;
using SpeechService = global::Alife.OfficialPlugins.SpeechService;
using PetService = global::Alife.OfficialPlugins.PetService;

namespace Alife.Modules.Context;

[PluginAttr("框架-Python可视化", "将 Python 的运行过程实时显示在聊天窗口中。")]
public class PythonConnector : PluginBase
{
    private readonly PythonService _service;
    private DialogContext? _dialogContext;
    private readonly Dictionary<object, DialogItem> _items = new();

    public PythonConnector(PythonService service)
    {
        _service = service;
    }

    public override Task StartAsync(Kernel kernel, global::ChatActivity chatActivity)
    {
        _dialogContext = chatActivity.PluginService.GetRequiredService<DialogContext>();
        _service.OnOutput += HandleOutput;
        _service.OnOutputUpdated += HandleUpdate;
        _service.OnFinished += HandleFinished;
        return Task.CompletedTask;
    }

    private void HandleOutput(object token, string content)
    {
        var item = new DialogItem {
            tool = "PythonService",
            content = content,
            isUser = false,
            isInputting = true,
            isDefaultHiding = true
        };
        _items[token] = item;
        _dialogContext?.AddMessage(item);
    }

    private void HandleUpdate(object token, string content)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.content = content;
            _dialogContext?.UpdateMessage(item);
        }
    }

    private void HandleFinished(object token)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.isInputting = false;
            _dialogContext?.UpdateMessage(item);
        }
    }
}

[PluginAttr("框架-语音可视化", "将语音识别和播报过程实时显示在聊天窗口中。")]
public class SpeechConnector : PluginBase
{
    private readonly SpeechService _service;
    private DialogContext? _dialogContext;
    private readonly Dictionary<object, DialogItem> _items = new();

    public SpeechConnector(SpeechService service)
    {
        _service = service;
    }

    public override Task StartAsync(Kernel kernel, global::ChatActivity chatActivity)
    {
        _dialogContext = chatActivity.PluginService.GetRequiredService<DialogContext>();
        _service.OnSpeechRecognized += (text) => {
            _dialogContext?.AddMessage(new DialogItem { content = text, isUser = true });
        };
        _service.OnSpeakOutput += HandleOutput;
        _service.OnSpeakFinished += HandleFinished;
        return Task.CompletedTask;
    }

    private void HandleOutput(object token, string content)
    {
        var item = new DialogItem { tool = "speak", content = content, isInputting = true };
        _items[token] = item;
        _dialogContext?.AddMessage(item);
    }

    private void HandleFinished(object token)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.isInputting = false;
            _dialogContext?.UpdateMessage(item);
        }
    }
}

[PluginAttr("框架-聊天可视化", "将 AI 的核心回复过程显示在聊天窗口中。")]
public class ChatConnector : PluginBase
{
    private readonly ChatService _service;
    private DialogContext? _dialogContext;
    private readonly Dictionary<object, DialogItem> _items = new();

    public ChatConnector(ChatService service)
    {
        _service = service;
    }

    public override Task StartAsync(Kernel kernel, global::ChatActivity chatActivity)
    {
        _dialogContext = chatActivity.PluginService.GetRequiredService<DialogContext>();
        _service.OnAssistantChat += HandleOutput;
        _service.OnAssistantChatUpdated += HandleUpdate;
        _service.OnAssistantChatFinished += HandleFinished;

        _dialogContext.MessageAdded += (item) => {
            if (item.isUser && item.content != null) _service.OnUserMessageAdded(item.content);
        };

        return Task.CompletedTask;
    }

    private void HandleOutput(object token, string content)
    {
        var item = new DialogItem { content = content, isUser = false, isInputting = true };
        _items[token] = item;
        _dialogContext?.AddMessage(item);
    }

    private void HandleUpdate(object token, string content)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.content += content;
            _dialogContext?.UpdateMessage(item);
        }
    }

    private void HandleFinished(object token)
    {
        if (_items.TryGetValue(token, out var item))
        {
            item.isInputting = false;
            _dialogContext?.UpdateMessage(item);
        }
    }
}

[PluginAttr("框架-桌宠可视化", "将桌宠的回复显示在聊天窗口中。")]
public class PetConnector : PluginBase
{
    private readonly PetService _service;
    private DialogContext? _dialogContext;

    public PetConnector(PetService service)
    {
        _service = service;
    }

    public override Task StartAsync(Kernel kernel, global::ChatActivity chatActivity)
    {
        _dialogContext = chatActivity.PluginService.GetRequiredService<DialogContext>();
        _service.OnPetChatReceived += (text) => {
            _dialogContext?.AddMessage(new DialogItem { content = text, isUser = true });
        };
        return Task.CompletedTask;
    }
}
