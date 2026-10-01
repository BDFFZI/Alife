using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Foundation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public class CharacterSystem
{
    public event Action? CharacterListChanged;
    public event Func<Character, Task>? CharacterChangedAsync;

    public List<Character> GetAllCharacters()
    {
        return characters;
    }

    public Character CreateCharacter(string name)
    {
        name = SanitizeName(name);

        // 如果重名，补充后缀
        string uniqueName = name;
        int index = 1;
        while (characters.Any(c => c.Name == uniqueName))
            uniqueName = $"{name}_{index++}";

        Character character = new Character {
            Name = uniqueName
        };
        characters.Add(character);
        CharacterListChanged?.Invoke();
        return character;

        static string SanitizeName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
                name = name.Replace(c, '_');
            return name;
        }
    }
    public void DeleteCharacter(Character character)
    {
        storageSystem.DeleteObject($"{character.StorageKey}/index");
        characters.Remove(character);
        CharacterListChanged?.Invoke();
    }
    public async Task SaveCharacter(Character character)
    {
        JObject jObject = JObject.FromObject(character);
        storageSystem.SetObject($"Character/{character.Name}/index", jObject);

        if (CharacterChangedAsync != null)
        {
            try
            {
                await Task.WhenAll(CharacterChangedAsync.GetInvocationList()
                    .Cast<Func<Character, Task>>()
                    .Select(func => func(character)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }
    public async Task LoadCharacter(Character character)
    {
        string json = await File.ReadAllTextAsync(Path.Combine(AlifePath.StorageFolderPath, "Character", character.Name, "index.json"));
        JsonConvert.PopulateObject(json, character,new JsonSerializerSettings() {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        });

        if (CharacterChangedAsync != null)
        {
            try
            {
                await Task.WhenAll(CharacterChangedAsync.GetInvocationList()
                    .Cast<Func<Character, Task>>()
                    .Select(func => func(character)));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
    }

    readonly StorageSystem storageSystem;
    readonly List<Character> characters;

    public CharacterSystem(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        characters = new List<Character>();

        string[] folder = storageSystem.GetSubFolders("Character");
        foreach (string name in folder)
        {
            Character? character = LoadCharacter(name);
            if (character != null)
                characters.Add(character);
        }
    }

    Character? LoadCharacter(string name)
    {
        JObject? jObject = storageSystem.GetObject<JObject>(Path.Combine("Character", name, "index"));
        if (jObject == null)
            return null;
        return jObject.ToObject<Character>();
    }
}