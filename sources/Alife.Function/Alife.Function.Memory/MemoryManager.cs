using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;

namespace Alife.Function.Memory;

public record MemoryMeta(int Level, DateTime StartTime, DateTime EndTime)
{
    public string Name => $"{Level}-{StartTime:yyyyMMddHHmmss}-{EndTime:yyyyMMddHHmmss}";
}

/// <summary>
/// 记忆核心管理器。协调存储、索引和压缩逻辑。
/// 实现层级化索引关系：每个摘要记录都描述了其涵盖的对话范围和时间跨度。
/// </summary>
public class MemoryManager
{
    public MemoryManager(HistoryCompressor compressor, TextVectorizer vectorizer, string storagePath,
        int compressionThreshold, int compressionCount, int maxCompressionLevel)
    {
        this.compressionThreshold = compressionThreshold;
        this.compressionCount = compressionCount;
        this.maxCompressionLevel = maxCompressionLevel;

        this.compressor = compressor;
        historyStoragePath = $"{storagePath}/History.json";
        memoryStorage = new MemoryStorage(storagePath, vectorizer);

        if (!Directory.Exists(storagePath))
            Directory.CreateDirectory(storagePath);
    }

    public async Task<bool> Filter(ChatHistoryAgentThread chatHistoryAgentThread)
    {
        ChatHistory chatHistory = chatHistoryAgentThread.ChatHistory;

        //跳过系统提示词
        int contentIndex = 0;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            if (chatHistory[contentIndex].Role != AuthorRole.System)
                break;
        }

        //遍历每个层级的聊天记录
        int areaLevel = -1;
        int areaStart = -1;
        int areaCount = -1;
        int areaCompressionThreshold = -1;
        int areaCompressionCount = -1;
        for (; contentIndex < chatHistory.Count; contentIndex++)
        {
            ChatMessageContent currentContent = chatHistory[contentIndex];
            MemoryMeta currentMemoryMeta = GetMemoryMetaData(currentContent);

            int currentLevel = currentMemoryMeta.Level;
            if (areaLevel != currentLevel)
            {
                //进入一个区域
                areaLevel = currentLevel;
                areaStart = contentIndex;
                areaCount = 1;
                areaCompressionThreshold = areaLevel == 0 ? compressionThreshold : 4;
                areaCompressionCount = areaLevel == 0 ? compressionCount : 3;
            }
            else
            {
                areaCount++;
            }

            if (areaCount >= areaCompressionThreshold && areaLevel + 1 <= maxCompressionLevel)//压缩记忆
            {
                //确认压缩事件段和内容
                DateTime startTime = GetMemoryMetaData(chatHistory[areaStart]).StartTime;
                DateTime endTime = GetMemoryMetaData(chatHistory[areaStart + areaCompressionCount - 1]).EndTime;
                string fullContent = PickContent(chatHistory, areaStart, areaStart + areaCompressionCount);

                string range = $"从`{startTime}`到`{endTime}`期间的`{(areaLevel == 0 ? "非记忆存档内容" : $"{areaLevel}级记忆存档")}`";
                string? summary = await compressor.Compress(chatHistoryAgentThread, range);
                if (summary == null)
                    return false;

                //插入新增的记忆存档
                await SaveMemory(areaLevel + 1, startTime, endTime, summary, fullContent, chatHistory, areaStart);

                //移除被压缩记忆
                for (int index = areaStart + areaCompressionCount; index > areaStart; index--)
                    memoryMetaDatas.Remove(chatHistory[index]);
                chatHistory.RemoveRange(areaStart + 1, areaCompressionCount);

                return true;
            }
        }

        return false;
    }

    public async Task<string> InsertMemory(ChatHistory chatHistory, int level, string summary, string content, DateTime startTime, DateTime endTime)
    {
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
            if (currentLevel == level)
            {
                foundLevel = true;
                insertIndex = i + 1;
            }
            else if (foundLevel)
            {
                // 已经过了该层级的连续区域
                break;
            }
            else if (currentLevel < level)
            {
                // 还没找到该层级，但已经到了更低层级（说明该层级不存在，应插在更低层级之前）
                insertIndex = i;
                break;
            }
        }

        if (insertIndex == -1)
            insertIndex = chatHistory.Count;

        return await SaveMemory(level, startTime, endTime, summary, content, chatHistory, insertIndex);
    }

    public void RemoveMemory(ChatHistory chatHistory, ChatMessageContent content)
    {
        if (chatHistory.Remove(content))
        {
            memoryMetaDatas.Remove(content);
        }
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

        foreach (HistoryRecord historyRecord in history)
        {
            ChatMessageContent chatMessageContent = new(historyRecord.Role, historyRecord.Content);
            chatHistory.Add(chatMessageContent);
            memoryMetaDatas.Add(chatMessageContent, historyRecord.MemoryMeta);
        }
    }

    public Task<string?> ReadMemory(string index)
    {
        int level = int.Parse(index[..index.IndexOf('-')]);
        return memoryStorage.LoadAsync(level, index);
    }

    public async Task<(List<SearchResult> Results, int Total)> SearchMemory(int level, string keyword, string? question, int count, int offset, DateTime? startTime, DateTime? endTime)
    {
        return await memoryStorage.SearchAsync(level, keyword, question, count, offset, startTime, endTime);
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

    readonly int compressionThreshold;
    readonly int compressionCount;
    readonly int maxCompressionLevel;
    readonly HistoryCompressor compressor;
    readonly MemoryStorage memoryStorage;
    readonly string historyStoragePath;
    readonly Dictionary<ChatMessageContent, MemoryMeta> memoryMetaDatas = new Dictionary<ChatMessageContent, MemoryMeta>();

    async Task<string> SaveMemory(int level, DateTime startTime, DateTime endTime, string summary, string content, ChatHistory chatHistory, int insertIndex)
    {
        MemoryMeta memoryMeta = new(level, startTime, endTime);
        string name = memoryMeta.Name;

        //归档到数据库
        await memoryStorage.SaveAsync(name, memoryMeta.Level, summary, content, memoryMeta.StartTime, memoryMeta.EndTime);

        //插入到上下文
        summary = $"""
                   [记忆存档({name})]
                   {summary}
                   """;
        ChatMessageContent compressedContent = new(AuthorRole.Assistant, summary);
        chatHistory.Insert(insertIndex, compressedContent);
        memoryMetaDatas[compressedContent] = new MemoryMeta(level, startTime, endTime);

        Console.WriteLine($"保存记忆：{name}");
        return name;
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
