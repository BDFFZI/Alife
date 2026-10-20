namespace Alife.Foundation;

public struct CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; }
    public string StandardError { get; init; }
}

public interface IAlifeFoundation
{
    public string AppVersion { get; }
    public string StorageFolderPath { get; }
    public string RuntimeFolderPath { get; }
}