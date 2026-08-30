using System.Threading;
using System.Threading.Tasks;

namespace BDFFZI.VibeCode.GameCompanion;

/// <summary>
/// 文本开关采样器：OCR 识别指定区域，当识别内容满足匹配要求时输出 "true"，否则输出 "false"。
/// <see cref="Value"/> 恒为 "true"（匹配）或 "false"（不匹配）的布尔状态；
/// 状态翻转时标记更新时间供防抖，框架据状态变化推送。
/// </summary>
[Collector(typeof(TextSwitchConfig), "文本开关",
    Ui = """
        <div class="t-specific-row"><label>OCR 区域</label><span data-region="Region"></span></div>
        <div class="t-specific-row"><label>匹配要求</label><input data-regex="RegexFilter" placeholder="匹配时输出 true，如 开始" /></div>
        """)]
public sealed class TextSwitchCollector(TextSwitchConfig config) : CollectorBase
{
    bool matched;
    string lastRawOcr = "";

    public override CollectConfigBase Config => config;
    public override string? Value => matched ? "true" : "false";
    public override string? DebugValue => lastRawOcr.Length == 0 ? null : lastRawOcr;

    public override async Task Update(GameContext ctx, CancellationToken ct)
    {
        if (ctx.Frame == null)
        {
            UpdateState(false, "");
            return;
        }

        using var crop = ctx.Frame.Crop(config.Region);
        if (crop == null)
        {
            UpdateState(false, "");
            return;
        }

        string? text = await OcrService.RecognizeAsync(crop, ct);
        string normalized = text == null ? "" : Normalize(text);
        lastRawOcr = normalized;

        bool nowMatched = false;
        if (!string.IsNullOrWhiteSpace(config.RegexFilter))
        {
            try
            {
                nowMatched = System.Text.RegularExpressions.Regex.Match(normalized, config.RegexFilter).Success;
            }
            catch (System.ArgumentException) { }
        }
        else if (normalized.Length > 0)
        {
            nowMatched = true;
        }

        if (nowMatched != matched)
        {
            matched = nowMatched;
        }
    }

    void UpdateState(bool newMatched, string raw)
    {
        lastRawOcr = raw;
        if (newMatched != matched)
            matched = newMatched;
    }

    static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", "");
}
