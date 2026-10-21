namespace Alife.Foundation;

public interface IAlifeFoundation
{
    public string AppVersion { get; }
    public string StorageFolderPath { get; }
    public string RuntimeFolderPath { get; }
    public string TempFolderPath { get; }
}