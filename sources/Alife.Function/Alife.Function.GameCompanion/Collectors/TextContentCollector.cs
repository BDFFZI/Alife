using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.GameCompanion.Screen;

namespace Alife.Function.GameCompanion.Collectors;

/// <summary>
/// 文本内容采样器：OCR 识别指定区域，经正则过滤后输出文本。
/// <see cref="Value"/> 为过滤后的文本（未识别到或无匹配时为 null）；
/// <see cref="DebugValue"/> 为过滤前的原始识别文本。值变化时标记更新时间供防抖。
/// 有效 = 识别到非空文本且命中过滤。
/// </summary>
public sealed class TextContentCollector(TextContentConfig config) : CollectorBase
{
    string? value;
    string? debugValue;

    static TextContentCollector()
    {
        CollectorRegistry.Register<TextContentConfig>(
            "文本内容",
            cfg => new TextContentCollector(cfg),
            cfg => !cfg.Region.IsEmpty,
            ui: """
                <div class="t-specific-row"><label>OCR 区域</label><span data-region="Region"></span></div>
                <div class="t-specific-row"><label>内容要求</label><input data-regex="RegexFilter" placeholder="正则表达式，如 \\d+" /></div>
                """);
    }

    public override string Name => config.Name;
    public override double DebounceSeconds => config.DebounceSeconds;
    public override string? Value => value;
    public override string? DebugValue => debugValue;

    public override async Task Update(GameContext ctx, CancellationToken ct)
    {
        value = null;
        debugValue = null;

        ScreenFrame? frame = ctx.Frame;
        if (frame is null)
            return;

        using var crop = frame.Crop(config.Region);
        if (crop is null)
            return;

        string? text = await OcrService.RecognizeAsync(crop, ct);
        if (text is null)
            return;

        string normalized = Normalize(text);
        if (normalized.Length == 0)
            return;

        debugValue = normalized;
        string? filtered = ApplyFilter(normalized, config.RegexFilter);
        if (filtered is not null && filtered.Length != 0)
        {
            if (filtered != value)
                value = filtered;
        }
    }

    static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", "");

    static string? ApplyFilter(string text, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return text;
        try
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
            if (matches.Count == 0)
                return null;
            var result = new System.Text.StringBuilder();
            foreach (System.Text.RegularExpressions.Match m in matches)
                result.Append(m.Value);
            return result.ToString();
        }
        catch (ArgumentException)
        {
            return text;
        }
    }
}