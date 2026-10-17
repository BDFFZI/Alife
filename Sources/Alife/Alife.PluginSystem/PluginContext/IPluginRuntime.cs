namespace Alife.Framework;

public interface IPluginRuntime
{
    string GetNuGetPropertyGroup();
    void RestoreNuGetPackages(string projectFile);
}
