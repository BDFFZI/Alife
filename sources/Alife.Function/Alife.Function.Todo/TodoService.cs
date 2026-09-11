using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.MessageFilter;

namespace Alife.Function.Todo;

[Module("待办事项",
    "为 AI 提供任务规划与进度追踪能力，可添加、完成和清除待办事项，并在消息中自动附带当前待办清单。",
    editorUI: typeof(TodoServiceUI),
    defaultCategory: "Alife 官方/实用工具")]
public class TodoService(
    XmlFunctionCaller functionCaller,
    StorageSystem storageSystem,
    MessageFilterService messageFilterService,
    Interactor<TodoService> interactor) :
    ChatBehaviour
{
    public IReadOnlyList<TodoItem> Items => manager.Items;

    public void ReplaceTodos(IEnumerable<TodoItem> items)
    {
        manager.Load(items);
        Save();
    }

    [XmlFunction(FunctionMode.OneShot, "todoadd")]
    public void Add(string content)
    {
        TodoItem item = manager.Add(content);
        Save();
        PokeWithList($"已添加待办：{item.Content}");
    }

    [XmlFunction(FunctionMode.OneShot, "todocomplete")]
    public void Complete([Description("待办序号，从1开始")] int index)
    {
        TodoItem item = manager.Complete(index);
        Save();
        PokeWithList($"已完成待办：{item.Content}");
    }

    [XmlFunction(FunctionMode.OneShot, "todoclear")]
    public void Clear(TodoClearMode mode = TodoClearMode.Completed)
    {
        manager.Clear(mode);
        Save();

        string list = manager.Format();
        interactor.Poke(string.IsNullOrEmpty(list)
            ? "待办事项已清空"
            : $"已清除待办事项\n\n{list}");
    }

    readonly TodoManager manager = new();

    protected override Task OnAwake()
    {
        manager.Load(storageSystem.GetObject<List<TodoItem>>(GetStoragePath()));
        messageFilterService.AddMessageReplyGuidance(OnGuidance, DestroyCancellationToken);

        XmlHandler xmlHandler = new(this) {
            Description = "当用户向你发起需求，例如功能开发，分析检索内容，等各种任务时，请养成一个良好科学的解题习惯。比如先收集资料明确需求，然后分析拆解实现步骤，再用待办事项功能规划好你的计划，然后逐步执行它，从而大幅提高你完成任务的成功率。",
        };
        functionCaller.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken);

        return Task.CompletedTask;
    }


    string GetStoragePath()
    {
        return $"{Character.StorageKey}/Todo";
    }

    void Save()
    {
        storageSystem.SetObject(GetStoragePath(), manager.Snapshot());
    }

    void PokeWithList(string message)
    {
        string list = manager.Format();
        interactor.Poke(string.IsNullOrEmpty(list) ? message : $"{message}\n{list}");
    }

    string OnGuidance()
    {
        string list = manager.Format();
        return string.IsNullOrEmpty(list)
            ? ""
            : list + "\n(如果尚有代办事项未完成，请完成它们，否则请清理它们)";
    }
}