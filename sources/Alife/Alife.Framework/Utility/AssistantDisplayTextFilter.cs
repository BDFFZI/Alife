using System.Text.RegularExpressions;

namespace Alife.Framework;

public static partial class AssistantDisplayTextFilter
{
    const string ExpressionSadPlaceholder = "%%ALIFE_EXPRESSION_SAD%%";
    const string ExpressionThinkingPlaceholder = "%%ALIFE_EXPRESSION_THINKING%%";

    public static string Filter(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return string.Empty;

        string text = rawText;
        text = SpeakRegex().Replace(text, match => {
            string content = StripTags(match.Groups[1].Value).Trim();
            return string.IsNullOrWhiteSpace(content) ? string.Empty : $"「{content}」";
        });

        text = ExpressionSadRegex().Replace(text, ExpressionSadPlaceholder);
        text = ExpressionThinkingRegex().Replace(text, ExpressionThinkingPlaceholder);

        string previous;
        do
        {
            previous = text;
            text = PairedTagRegex().Replace(text, string.Empty);
        } while (text != previous);

        text = SelfClosingTagRegex().Replace(text, string.Empty);
        text = AnyTagOrFragmentRegex().Replace(text, string.Empty);

        return text
            .Replace(ExpressionSadPlaceholder, "<委屈>")
            .Replace(ExpressionThinkingPlaceholder, "<思考状>")
            .Trim();
    }

    static string StripTags(string text)
    {
        string previous;
        do
        {
            previous = text;
            text = PairedTagRegex().Replace(text, string.Empty);
        } while (text != previous);

        text = SelfClosingTagRegex().Replace(text, string.Empty);
        return AnyTagOrFragmentRegex().Replace(text, string.Empty);
    }

    [GeneratedRegex("<speak\\b[^>]*>(.*?)</speak\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SpeakRegex();

    [GeneratedRegex("<expression\\b(?=[^>]*\\boption\\s*=\\s*[\"']委屈[\"'])[^>]*/\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExpressionSadRegex();

    [GeneratedRegex("<expression\\b(?=[^>]*\\boption\\s*=\\s*[\"']思考状[\"'])[^>]*/\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExpressionThinkingRegex();

    [GeneratedRegex("<([A-Za-z][\\w:.-]*)\\b[^>]*>.*?</\\1\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PairedTagRegex();

    [GeneratedRegex("<[A-Za-z][^<>]*/\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelfClosingTagRegex();

    [GeneratedRegex("<[^>]*(>|$)", RegexOptions.Singleline)]
    private static partial Regex AnyTagOrFragmentRegex();
}
