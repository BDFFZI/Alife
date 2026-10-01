namespace Alife;

public interface IAlifeClient
{
    void Quit();
    void Exit();
    Task<string?> ShowSelectDirectoryDialog();
}