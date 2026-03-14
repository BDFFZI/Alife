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

    public void SaveCharacters()
    {
        SaveCharacterManifest();
        foreach (Character character in characters)
            SaveCharacter(character);
    }
    void SaveCharacterManifest()
    {
        storageSystem.SetObject("CharacterSystem/CharacterManifest", characters.Select(character => character.ID));
    }
    public void SaveCharacter(Character character)
    {
        JObject jObject = new JObject {
            [nameof(Character.ID)] = character.ID,
            [nameof(Character.Name)] = character.Name,
            [nameof(Character.Prompt)] = character.Prompt,
            [nameof(Character.AutoActivate)] = character.AutoActivate,
            [nameof(Character.Plugins)] = JArray.FromObject(character.Plugins.Select(t => t.AssemblyQualifiedName ?? t.FullName)),
        };
        storageSystem.SetObject("CharacterSystem/" + character.ID, jObject);
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

        return new Character() {
            ID = characterID,
            Name = jObject[nameof(Character.Name)]?.Value<string>() ?? "未命名角色",
            Prompt = jObject[nameof(Character.Prompt)]?.Value<string>() ?? "你是一位有用的助手。",
            AutoActivate = jObject[nameof(Character.AutoActivate)]?.Value<bool>() ?? false,
            Plugins = jObject[nameof(Character.Plugins)]?
                .Select(jt => pluginSystem.GetPlugin(jt.ToString()))
                .Where(t => t != null).Cast<Type>()
                .ToHashSet() ?? new HashSet<Type>(),
        };
    }
}
