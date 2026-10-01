using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client;

public class ClientConfigStore(StorageSystem storageSystem, CharacterSystem characterSystem)
{
    public Character? GetLastCharacter()
    {
        string? lastCharacter = storageSystem.GetProperty("LastCharacter");
        Character? character = characterSystem.GetAllCharacters().FirstOrDefault(character => character.Name == lastCharacter);
        return character;
    }
    public void SetLastCharacter(Character character)
    {
        storageSystem.SetProperty("LastCharacter", character.Name);
    }

    public bool GetAutoStart()
    {
        try
        {
            string result = AlifeUtility.Command("schtasks", "/query /tn \"Alife\" /fo csv /nh").StandardOutput;
            return result.Contains("Alife");
        }
        catch
        {
            return false;
        }
    }
    public void SetAutoStart(bool value)
    {
        AlifeUtility.Command("schtasks", value
            ? $"/create /tn \"Alife\" /tr \"\\\"{AlifePath.AppPath}\\\"\" /sc onlogon /rl highest /f"
            : "/delete /tn \"Alife\" /f");
    }

    public AlifeClient.InitConfig GetInitConfig()
    {
        return storageSystem.GetObject("ClientConfig", Default)!;
    }
    public void SetInitConfig(AlifeClient.InitConfig config)
    {
        storageSystem.SetObject("ClientConfig", config);
    }

    AlifeClient.InitConfig Default => new() {
        FetchOnlineInfo = true,
        LoadLocalPlugins = true,
        EnsureEnvironment = true,
        McpServer = true,
        McpServerPort = 18765,
    };
}