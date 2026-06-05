namespace Alife.Framework;

public static class ChatMessageDisplayPolicy
{
    public static bool ShouldDisplayAssistantMessage(
        string? content,
        string? reasoning,
        bool isInputting,
        bool showReasoning)
    {
        if (isInputting)
            return true;

        return !string.IsNullOrWhiteSpace(content) ||
               (showReasoning && !string.IsNullOrWhiteSpace(reasoning));
    }
}
