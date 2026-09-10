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

    [XmlFunction(FunctionMode.Content, "todoadd")]
    public void Add(XmlExecutorContext context)
    {
        if (context.CallMode != CallMode.Closing)
            return;

        TodoItem item = manager.Add(context.FullContent);
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
        messageFilterService.AddMessageReplyGuidance(manager.Format, DestroyCancellationToken);

        XmlHandler xmlHandler = new(this) {
            Description = "当你需要进行复杂任务（如插件开发，需求研究）时使用，他能让你更加科学有规划执行长任务，从而大幅提高成功率。",
            Explanation = """
                          待办事项用于管理需要多步骤完成的任务，清单按添加顺序排列，并会自动附带在你收到的消息中。

                          用法示例
                          ```
                          <todoadd>买牛奶</todoadd>              # 添加待办
                          <todocomplete index="1"/>              # 完成第1项
                          <todoclear/>                           # 仅清除已完成项
                          <todoclear mode="All"/>                # 清空全部
                          ```

                          使用提示
                          - 完成通过序号定位，序号以最新清单为准。
                          - 清单会自动出现在你收到的消息中，无需专门查询。
                          """
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);
        functionCaller.AddPlainAreas("todoadd");

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
}
