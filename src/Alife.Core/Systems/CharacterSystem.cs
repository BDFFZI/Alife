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
    }
    public void DeleteCharacter(Character character)
    {
        characters.Remove(character);
        OnChanged?.Invoke();
    }

    public void SaveData()
    {
        SaveCharacters();
    }

    public CharacterSystem(StorageSystem storageSystem)
    {
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
            JObject? jObject = storageSystem.GetObject<JObject>("CharacterSystem/" + characterID);
            if (jObject == null)
                continue;

            characters.Add(new Character() {
                ID = characterID,
                Name = jObject[nameof(Character.Name)]?.Value<string>() ?? "未命名角色",
                Prompt = jObject[nameof(Character.Prompt)]?.Value<string>() ?? "你是一位有用的助手。",
                AutoActivate = jObject[nameof(Character.AutoActivate)]?.Value<bool>() ?? false,
                Plugins = jObject[nameof(Character.Plugins)]?
                    .Select(jt => Type.GetType(jt.ToString())!)
                    .Where(t => t != null)
                    .ToHashSet() ?? new HashSet<Type>(),
            });
        }
    }
    void SaveCharacters()
    {
        storageSystem.SetObject("CharacterSystem/CharacterManifest", characters.Select(character => character.ID));
        foreach (Character character in characters)
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
    }

    readonly StorageSystem storageSystem;
    readonly List<Character> characters;
}
