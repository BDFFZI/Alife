using Alife;
using Newtonsoft.Json.Linq;

public class CharacterSystem : IDisposable
{
    public event Action? OnChanged;

    public IEnumerable<Character> GetAllCharacters()
    {
        return characters;
    }
    public void CreateCharacter()
    {
        characters.Add(new Character());
        OnChanged?.Invoke();
        SaveCharacterManifest();
    }
    public void DeleteCharacter(Character character)
    {
        characters.Remove(character);
        OnChanged?.Invoke();
        SaveCharacterManifest();
    }
    public void SaveCharacter(Character character)
    {
        JObject jObject = JObject.FromObject(character);
        storageSystem.SetObject("CharacterSystem/" + character.ID, jObject);
    }
    public void SaveCharacters()
    {
        SaveCharacterManifest();
        foreach (Character character in characters)
            SaveCharacter(character);
    }

    readonly StorageSystem storageSystem;
    readonly PluginSystem pluginSystem;
    readonly List<Character> characters;

    public CharacterSystem(PluginSystem pluginSystem, StorageSystem storageSystem)
    {
        this.pluginSystem = pluginSystem;
        this.storageSystem = storageSystem;
        characters = new List<Character>();

        LoadCharacters();
    }
    public void Dispose()
    {
        SaveCharacters();
    }

    void LoadCharacters()
    {
        characters.Clear();

        string[] characterManifest = storageSystem.GetObject("CharacterSystem/CharacterManifest", Array.Empty<string>())!;
        foreach (string characterID in characterManifest)
        {
            Character? character = LoadCharacter(characterID);
            if (character != null)
                characters.Add(character);
        }
    }
    Character? LoadCharacter(string characterID)
    {
        JObject? jObject = storageSystem.GetObject<JObject>("CharacterSystem/" + characterID);
        if (jObject == null)
            return null;
        return jObject.ToObject<Character>();
    }

    void SaveCharacterManifest()
    {
        storageSystem.SetObject("CharacterSystem/CharacterManifest", characters.Select(character => character.ID));
    }
}
