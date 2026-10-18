namespace Alife.Framework;

public abstract class PipEnvironmentInstaller : IEnvironmentInstaller
{
    public string EnvironmentName => "pip";
    public async Task InstallEnvironment(IEnumerable<KeyValuePair<string, string>> environment)
    {
        await InstallRequirements(environment);
    }

    protected abstract Task InstallRequirements(IEnumerable<KeyValuePair<string, string>> items);
}