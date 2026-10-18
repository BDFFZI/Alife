using System.Threading.Tasks;

namespace Alife.Foundation;

public struct CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; }
    public string StandardError { get; init; }
}

public interface IAlifeFoundation
{
    public Task OnInitializedAsync() => Task.CompletedTask;
    public string AppVersion { get; }
    public string StorageFolderPath { get; }
    public string RuntimeFolderPath { get; }
    public CommandResult Command(string fileName, string arguments);
}