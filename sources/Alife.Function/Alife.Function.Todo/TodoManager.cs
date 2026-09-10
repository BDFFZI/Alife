using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Alife.Function.Todo;

public class TodoItem
{
    public string Content { get; set; } = "";
    public bool Completed { get; set; }
}

public enum TodoClearMode
{
    Completed,
    All,
}

public class TodoManager
{
    public IReadOnlyList<TodoItem> Items => items;

    public void Load(IEnumerable<TodoItem>? loadedItems)
    {
        items.Clear();
        if (loadedItems != null)
            items.AddRange(loadedItems);
    }

    public List<TodoItem> Snapshot()
    {
        return items.Select(item => new TodoItem {
            Content = item.Content,
            Completed = item.Completed
        }).ToList();
    }

    public TodoItem Add(string content)
    {
        content = content.Trim();
        if (content.Length == 0)
            throw new Exception("待办内容不能为空");

        TodoItem item = new() {
            Content = content
        };
        items.Add(item);

        return item;
    }

    public TodoItem Complete(int index)
    {
        TodoItem target = Resolve(index);
        target.Completed = true;

        return target;
    }

    public void Clear(TodoClearMode mode)
    {
        if (mode == TodoClearMode.Completed)
            items.RemoveAll(item => item.Completed);
        else
            items.Clear();
    }

    public string Format()
    {
        if (items.Count == 0)
            return "";

        int completed = items.Count(item => item.Completed);
        StringBuilder builder = new();
        builder.AppendLine($"当前待办事项（{completed}/{items.Count} 已完成）：");
        for (int index = 0; index < items.Count; index++)
        {
            TodoItem item = items[index];
            builder.AppendLine($"{index + 1}. [{(item.Completed ? "x" : " ")}] {item.Content}");
        }

        return builder.ToString().TrimEnd();
    }

    TodoItem Resolve(int index)
    {
        if (index < 1 || index > items.Count)
            throw new Exception($"待办序号 {index} 超出范围（当前共 {items.Count} 项）");

        return items[index - 1];
    }

    readonly List<TodoItem> items = new();
}
