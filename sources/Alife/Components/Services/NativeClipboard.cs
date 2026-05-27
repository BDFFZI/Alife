namespace Alife.Components.Services;

/// <summary>
/// Provides access to the native system clipboard.
/// </summary>
public static class NativeClipboard
{
    public static string? GetPastedContent()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                return System.Windows.Clipboard.GetText();
        }
        catch
        {
            // Clipboard access may fail in some contexts
        }
        return null;
    }
}
