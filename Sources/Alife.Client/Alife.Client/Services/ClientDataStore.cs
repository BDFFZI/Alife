using Alife.Foundation;
using Alife.Framework;

namespace Alife.Client;

public class ClientDataStore(StorageSystem storageSystem, CharacterSystem characterSystem)
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
}