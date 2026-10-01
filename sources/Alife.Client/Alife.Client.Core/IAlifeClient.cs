namespace Alife.Client.Core;

public interface IAlifeClient
{
    void Quit();
    void Exit();
    Task<string?> ShowSelectDirectoryDialog();
}