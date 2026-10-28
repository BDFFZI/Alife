using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Alife.Function.TokenStatistics;

/// <summary>
/// 对话记录的按月 JSONL 存储。目录：{recordsFolder}\YYYY-MM.jsonl，每行一条 ChatRecord。
/// 按月分文件，便于 90 天过期时整月删除，也便于分页时只读需要的月份。
/// 超过保留期的整月文件在写入前自动清理。
/// </summary>
public sealed class ChatRecordStore
{
    public const int DefaultRetentionDays = 90;

    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    readonly string recordsFolder;
    readonly int retentionDays;

    /// <param name="recordsFolder">本角色的记录目录（如 Storage\Character\真央\TokenStatistics\records）。</param>
    /// <param name="retentionDays">保留天数，默认 90。</param>
    public ChatRecordStore(string recordsFolder, int retentionDays = DefaultRetentionDays)
    {
        this.recordsFolder = recordsFolder;
        this.retentionDays = retentionDays;
    }

    /// <summary>追加一条记录到其所属月份的文件，并清理过期月份。</summary>
    public void Append(ChatRecord record)
    {
        Directory.CreateDirectory(recordsFolder);
        string line = JsonSerializer.Serialize(record, JsonOptions);
        File.AppendAllText(MonthFilePath(record.Time), line + Environment.NewLine);
        PruneExpired(DateTimeOffset.Now);
    }

    /// <summary>读取时间范围内的所有记录（按时间升序）。范围按北京时间的日期边界解释。</summary>
    public IReadOnlyList<ChatRecord> ReadRange(DateTimeOffset from, DateTimeOffset to)
    {
        List<ChatRecord> result = new();
        foreach (string file in MonthFilesBetween(from, to))
        {
            foreach (string line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                ChatRecord? record = JsonSerializer.Deserialize<ChatRecord>(line, JsonOptions);
                if (record != null && record.Time >= from && record.Time < to)
                    result.Add(record);
            }
        }
        return result.OrderBy(record => record.Time).ToList();
    }

    /// <summary>删除早于保留期起点所在月份之前的所有月文件。保留期内的整月保留，不做行级截断。</summary>
    public void PruneExpired(DateTimeOffset now)
    {
        if (Directory.Exists(recordsFolder) == false)
            return;

        DateTimeOffset cutoff = now.AddDays(-retentionDays);
        string cutoffMonthKey = MonthKey(cutoff);

        foreach (string file in Directory.GetFiles(recordsFolder, "*.jsonl"))
        {
            string monthKey = Path.GetFileNameWithoutExtension(file);
            // 文件名为 YYYY-MM，字典序即时间序；严格早于截止月的整月删除
            if (string.CompareOrdinal(monthKey, cutoffMonthKey) < 0)
                File.Delete(file);
        }
    }

    string MonthFilePath(DateTimeOffset time) => Path.Combine(recordsFolder, MonthKey(time) + ".jsonl");

    static string MonthKey(DateTimeOffset time) => time.ToString("yyyy-MM");

    IEnumerable<string> MonthFilesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (Directory.Exists(recordsFolder) == false)
            yield break;

        string fromKey = MonthKey(from);
        string toKey = MonthKey(to);
        foreach (string file in Directory.GetFiles(recordsFolder, "*.jsonl").OrderBy(path => path, StringComparer.Ordinal))
        {
            string monthKey = Path.GetFileNameWithoutExtension(file);
            if (string.CompareOrdinal(monthKey, fromKey) >= 0 && string.CompareOrdinal(monthKey, toKey) <= 0)
                yield return file;
        }
    }
}
