namespace Alife.Framework;

public enum ChatInputVisibility
{
    Visible,
    Internal
}

public sealed class ChatInputSentEventArgs(string message, ChatInputVisibility visibility)
{
    public string Message { get; } = message;
    public ChatInputVisibility Visibility { get; } = visibility;
    public bool DisplayInputMessageInChat => Visibility == ChatInputVisibility.Visible;
}
