using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace Alife.Function.Memory;

public record MemoryMeta(int Level, DateTime StartTime, DateTime EndTime)
{
    public MemoryIndex ToMemoryIndex()
    {
        return new MemoryIndex(Level, $"{Level}-{StartTime:yyyyMMddHHmmss}-{EndTime:yyyyMMddHHmmss}");
    }
}

/// <summary>
/// 压缩计划：在副本上分析并归纳完成后，用于对真实上下文进行原子替换。
/// </summary>
public record MemoryCompressionPlan(
    int InsertIndex,
    int RemoveCount,
    int NewLevel,
    DateTime StartTime,
    DateTime EndTime,
    string? Summary,
    string FullContent,
    ChatMessageContent HeadAnchor,
    ChatMessageContent TailAnchor);

public partial class MemoryManager
{
    public static string GetMemoryTag(MemoryIndex index)
    {
        return $"[记忆存档({index})]";
    }
    public static string GetMemoryTag(string summary)
    {
        return Regex.Match(summary, @"^\[记忆存档\([^)]+\)\]").Value;
    }
    public static string RemoveMemoryTag(string summary)
    {
        return summary.Replace(GetMemoryTag(summary), "").Trim();
    }
}

/// <summary>
/// 记忆核心管理器。协调存储、索引和压缩逻辑。
/// 实现层级化索引关系：每个摘要记录都描述了其涵盖的对话范围和时间跨度。
/// </summary>
public partial class MemoryManager(
    HistoryCompressor compressor,
    string storagePath,
    int compressionThreshold,
    int compressionCount,
    int maxCompressionLevel)
{
    /// <summary>
    /// 阶段一：在快照副本上检查压缩条件并完成LLM归纳，不触碰真实上下文。
    /// 返回压缩计划；无需压缩时返回null。
    /// </summary>
    public async Task<MemoryCompressionPlan?> Analyze(ChatHistory chatHistory)
    {
        //跳过系统提示词
        int contentIndex = 0;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            if (chatHistory[contentIndex].Role != AuthorRole.System)
                break;
        }

        //遍历每个层级的聊天记录
        int areaLevel = int.MaxValue;
        int areaStart = contentIndex;
        int areaCount = 0;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            ChatMessageContent newContent = chatHistory[contentIndex];
            MemoryMeta newMemoryMeta = GetMemoryMetaData(newContent);
            int newLevel = newMemoryMeta.Level;

            if (areaLevel < newLevel)
            {
                //检测到逆序记录，需要合并升级来修复
                if (areaCount == 1)
                {
                    //异常区域仅一条记录，直接升级
                    ChatMessageContent lastContent = chatHistory[contentIndex - 1];
                    MemoryMeta lastMemoryMeta = GetMemoryMetaData(lastContent);
                    return new MemoryCompressionPlan(contentIndex - 1, 1, newLevel, lastMemoryMeta.StartTime,
                        lastMemoryMeta.EndTime, null!, null!, lastContent, lastContent);
                }

                //将异常区域压缩
                return await BuildCompressionPlan(chatHistory, areaStart, areaCount, newLevel);
            }

            if (areaLevel != newLevel)
            {
                //正常进入新区域
                areaLevel = newLevel;
                areaStart = contentIndex;
                areaCount = 1;
                continue;
            }

            areaCount++;

            if (areaLevel + 1 <= maxCompressionLevel && areaCount >= (areaLevel == 0 ? compressionThreshold : 4)) //压缩记忆
            {
                int areaCompressionCount = areaLevel == 0 ? compressionCount : 3;
                return await BuildCompressionPlan(chatHistory, areaStart, areaCompressionCount, areaLevel + 1);
            }
        }

        return null;

        async Task<MemoryCompressionPlan?> BuildCompressionPlan(ChatHistory snapshot, int areaStart, int count, int newLevel)
        {
            //确认压缩事件段和内容
            MemoryMeta startMemoryMeta = GetMemoryMetaData(snapshot[areaStart]);
            int oldLevel = startMemoryMeta.Level;
            DateTime startTime = startMemoryMeta.StartTime;
            DateTime endTime = GetMemoryMetaData(snapshot[areaStart + count - 1]).EndTime;
            string fullContent = PickContent(snapshot, areaStart, areaStart + count);

            string range;
            if (oldLevel == 0)
            {
                range = $"从`{startTime}`到`{endTime}`期间的普通对话记录";
            }
            else
            {
                range = "如下记忆存档：" + string.Join(',', snapshot
                    .Take(new Range(areaStart, areaStart + count))
                    .Select(content => content.Content == null ? null : GetMemoryTag(content.Content))
                    .Where(tag => !string.IsNullOrWhiteSpace(tag)));
            }

            string? summary = await compressor.Compress(new ChatHistoryAgentThread(snapshot), range);
            if (summary == null)
                return null;

            return new MemoryCompressionPlan(areaStart, count, newLevel, startTime, endTime, summary, fullContent,
                snapshot[areaStart], snapshot[areaStart + count - 1]);
        }
    }

    /// <summary>
    /// 阶段二：依据压缩计划对真实上下文进行原子替换（落库+插入存档+移除旧区段）。
    /// </summary>
    public async Task Apply(ChatHistory chatHistory, MemoryCompressionPlan plan)
    {
        //验证替换区域头尾对象未被修改（浅拷贝共享引用，按引用比对）
        if (plan.InsertIndex + plan.RemoveCount > chatHistory.Count
            || !ReferenceEquals(chatHistory[plan.InsertIndex], plan.HeadAnchor)
            || !ReferenceEquals(chatHistory[plan.InsertIndex + plan.RemoveCount - 1], plan.TailAnchor))
        {
            Console.WriteLine("记忆压缩放弃：快照与当前上下文不一致，留待下轮重新分析。");
            return;
        }

        //仅层级升级（逆序单条修复），无需归纳内容
        if (plan.Summary == null)
        {
            ChatMessageContent target = chatHistory[plan.InsertIndex];
            MemoryMeta oldMemoryMeta = GetMemoryMetaData(target);
            string? oldContent = await memoryStorage.LoadAsync(oldMemoryMeta.ToMemoryIndex());
            await WriteMemory(plan.NewLevel, plan.StartTime, plan.EndTime, RemoveMemoryTag(target.Content ?? ""), oldContent ?? "无内容", target);
            return;
        }

        await WriteMemory(plan.NewLevel, plan.StartTime, plan.EndTime, plan.Summary, plan.FullContent, chatHistory,
            plan.InsertIndex);

        //移除被压缩记忆
        for (int index = plan.InsertIndex + 1; index <= plan.InsertIndex + plan.RemoveCount; index++)
            memoryMetaDatas.Remove(chatHistory[index]);
        chatHistory.RemoveRange(plan.InsertIndex + 1, plan.RemoveCount);
    }

    public async Task WriteMemory(ChatHistory chatHistory, MemoryIndex memoryIndex, string summary, string content, DateTime? startTime = null,
        DateTime? endTime = null)
    {
        ChatMessageContent? chatMessageContent = FindMemory(chatHistory, memoryIndex);
        if (chatMessageContent != null)
        {
            if (GetMemoryMetaData(chatMessageContent).Level != memoryIndex.Level)
                throw new InvalidOperationException("存档等级不允许修改");

            chatMessageContent.Content = GetMemoryTag(memoryIndex) + "\n" + summary;
            await memoryStorage.SaveAsync(memoryIndex, content);
        }

        // 寻找插入位置（同级区域的最下方）
        int insertIndex = -1;
        int contentIndex = 0;
        // 跳过系统提示词
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            if (chatHistory[contentIndex].Role != AuthorRole.System)
                break;
        }

        // 查找该层级的最后一个位置
        bool foundLevel = false;
        for (int i = contentIndex; i < chatHistory.Count; i++)
        {
            int currentLevel = GetMemoryMetaData(chatHistory[i]).Level;
            if (currentLevel == memoryIndex.Level)
            {
                foundLevel = true;
                insertIndex = i + 1;
            }
            else if (foundLevel)
            {
                // 已经过了该层级的连续区域
                break;
            }
            else if (currentLevel < memoryIndex.Level)
            {
                // 还没找到该层级，但已经到了更低层级（说明该层级不存在，应插在更低层级之前）
                insertIndex = i;
                break;
            }
        }

        if (insertIndex == -1)
            insertIndex = chatHistory.Count;

        await WriteMemory(memoryIndex.Level, startTime ?? DateTime.Now, endTime ?? DateTime.Now, summary, content, chatHistory, insertIndex);
    }
    public Task<string?> ReadMemory(MemoryIndex memoryIndex)
    {
        return memoryStorage.LoadAsync(memoryIndex);
    }
    public ChatMessageContent? FindMemory(ChatHistory chatHistory, MemoryIndex memoryIndex)
    {
        string memoryTag = GetMemoryTag(memoryIndex);
        ChatMessageContent? chatMessageContent = chatHistory.FirstOrDefault(messageContent => messageContent.Content?.StartsWith(memoryTag) ?? false);
        return chatMessageContent;
    }
    public void RemoveMemory(ChatHistory chatHistory, ChatMessageContent content)
    {
        if (chatHistory.Remove(content))
            memoryMetaDatas.Remove(content);
    }


    public void SaveHistory(ChatHistory chatHistory)
    {
        List<HistoryRecord> history = new List<HistoryRecord>();

        foreach (ChatMessageContent chatMessageContent in chatHistory.Where(content => content.Role != AuthorRole.System))
        {
            if (chatMessageContent.Content == null)
                continue;
            history.Add(new HistoryRecord(
                chatMessageContent.Role,
                chatMessageContent.Content,
                GetMemoryMetaData(chatMessageContent)
            ));
        }

        File.WriteAllText(historyStoragePath, JsonConvert.SerializeObject(history, Formatting.Indented));
    }
    public void LoadHistory(ChatHistory chatHistory)
    {
        if (File.Exists(historyStoragePath) == false)
            return;

        string historyJson = File.ReadAllText(historyStoragePath);
        List<HistoryRecord>? history = JsonConvert.DeserializeObject<List<HistoryRecord>>(historyJson);
        if (history == null)
            return;

        //清除已有的非提示词内容，防止污染
        for (int i = chatHistory.Count - 1; i >= 0; i--)
        {
            if (chatHistory[i].Role != AuthorRole.System)
                chatHistory.RemoveAt(i);
        }

        foreach (HistoryRecord historyRecord in history)
        {
            ChatMessageContent chatMessageContent = new(historyRecord.Role, historyRecord.Content);
            chatHistory.Add(chatMessageContent);
            memoryMetaDatas.Add(chatMessageContent, historyRecord.MemoryMeta);
        }
    }


    public MemoryMeta GetMemoryMetaData(ChatMessageContent content)
    {
        if (memoryMetaDatas.TryGetValue(content, out MemoryMeta? data) == false)
        {
            data = new MemoryMeta(0, DateTime.Now, DateTime.Now);
            memoryMetaDatas.Add(content, data);
        }

        return data;
    }

    record HistoryRecord(AuthorRole Role, string Content, MemoryMeta MemoryMeta);

    readonly MemoryStorage memoryStorage = new(storagePath);
    readonly string historyStoragePath = $"{storagePath}/History.json";
    readonly Dictionary<ChatMessageContent, MemoryMeta> memoryMetaDatas = new();

    async Task WriteMemory(int level, DateTime startTime, DateTime endTime, string summary, string content, ChatHistory chatHistory,
        int insertIndex)
    {
        ChatMessageContent compressedContent = new(AuthorRole.Assistant, "");
        chatHistory.Insert(insertIndex, compressedContent);
        await WriteMemory(level, startTime, endTime, summary, content, compressedContent);
    }
    async Task WriteMemory(int level, DateTime startTime, DateTime endTime, string summary, string content,
        ChatMessageContent chatMessageContent)
    {
        MemoryMeta memoryMeta = new(level, startTime, endTime);
        MemoryIndex memoryIndex = memoryMeta.ToMemoryIndex();

        string memoryTag = GetMemoryTag(memoryIndex);
        chatMessageContent.Content = memoryTag + "\n" + summary;
        memoryMetaDatas[chatMessageContent] = new MemoryMeta(memoryIndex.Level, startTime, endTime);
        await memoryStorage.SaveAsync(memoryIndex, content);
    }

    string PickContent(ChatHistory chatHistory, int start, int end)
    {
        StringBuilder stringBuilder = new();

        for (int index = start; index < end; index++)
        {
            ChatMessageContent content = chatHistory[index];
            stringBuilder.AppendLine($"【{content.Role}】：\n{content.Content}\n");
        }

        return stringBuilder.ToString();
    }
}