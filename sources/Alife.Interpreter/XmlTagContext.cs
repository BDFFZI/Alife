namespace Alife.Interpreter;

public enum CallMode
{
    Opening,
    Content,
    Closing,
    OneShot
}
public class XmlTagContext
{
    public Stack<string> CallChain { get; init; }
    public CallMode CallMode { get; init; }
    public IDictionary<string, string> CallParams { get; init; }
    public string FullContent { get; init; } = "";
    public string ChipContent { get; set; } = "";
    public string? Fracture { get; init; }
}
