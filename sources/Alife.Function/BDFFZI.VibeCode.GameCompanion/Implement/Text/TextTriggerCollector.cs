using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 文本触发采样器：OCR 识别指定区域，当识别内容满足触发要求时记录一次触发。
/// 仅当匹配文本「变化」为新的匹配值时才触发一次（长显不重复触发）。
/// <see cref="Value"/> 返回本次累计的所有触发时间戳（逗号分隔，未触发时返回 null）；
/// 框架推送后 <see cref="Use"/> 清空。
/// </summary>
[Collector(typeof(TextTriggerConfig), "文本触发",
    Ui = """
        <div class="t-specific-row"><label>OCR 区域</label><span data-region="Region"></span></div>
        <div class="t-specific-row"><label>触发要求</label><input data-regex="RegexFilter" placeholder="识别到匹配内容时触发，如 \\d+" /></div>
        """)]
public sealed class TextTriggerCollector(TextTriggerConfig config) : CollectorBase
{
    readonly List<string> triggers = new();
    string lastRawOcr = "";
    bool lastMatched;
    DateTime lastMissTime = DateTime.MinValue;

    public override CollectConfigBase Config => config;
    public override string? Value => triggers.Count == 0 ? null : string.Join(",", triggers);
    public override string? DebugValue => lastRawOcr.Length == 0 ? null : lastRawOcr;

    public override void Use() => triggers.Clear();

    public override async Task Update(GameContext ctx, CancellationToken ct)
    {
        if (ctx.Frame == null)
        {
            lastRawOcr = "";
            lastMatched = false;
            return;
        }

        using var crop = ctx.Frame.Crop(config.Region);
        if (crop == null)
        {
            lastRawOcr = "";
            lastMatched = false;
            return;
        }

        string? text = await OcrService.RecognizeAsync(crop, ct);
        string normalized = text == null ? "" : Normalize(text);
        lastRawOcr = normalized;

        bool matched = false;
        if (!string.IsNullOrWhiteSpace(config.RegexFilter))
        {
            try
            {
                matched = System.Text.RegularExpressions.Regex.Match(normalized, config.RegexFilter).Success;
            }
            catch (ArgumentException) { }
        }
        else if (normalized.Length > 0)
        {
            matched = true;
        }

        DateTime now = DateTime.UtcNow;
        if (!matched && lastMatched)
            lastMissTime = now;

        if (matched && !lastMatched)
        {
            if ((now - lastMissTime).TotalSeconds >= Math.Max(0, config.DebounceSeconds))
                triggers.Add(DateTime.Now.ToString("HH:mm:ss"));
        }
        lastMatched = matched;
    }

    static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", "");
}
