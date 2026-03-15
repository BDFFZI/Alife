using Alife.Abstractions;
using Xunit;

namespace Alife.Core.Tests;

public class CoreSystemsTests
{
    [Fact]
    public void TestCharacterPersistence()
    {
        // 1. 初始化 dependencies
        StorageSystem storageSystem = new StorageSystem();
        PluginSystem pluginSystem = new PluginSystem(storageSystem);

        // 2. 初始化 CharacterSystem
        CharacterSystem characterSystem = new CharacterSystem(pluginSystem, storageSystem);
        int initialCount = characterSystem.GetAllCharacters().Count();

        // 3. 创建新角色
        characterSystem.CreateCharacter();
        int afterCreateCount = characterSystem.GetAllCharacters().Count();
        Assert.Equal(initialCount + 1, afterCreateCount);

        // 4. 保存数据
        characterSystem.SaveCharacters();

        // 5. 验证持久化 (模拟重启)
        CharacterSystem newCharacterSystem = new CharacterSystem(pluginSystem, storageSystem);
        int finalCount = newCharacterSystem.GetAllCharacters().Count();
        Assert.Equal(afterCreateCount, finalCount);
    }

    [Fact]
    public void TestCharacterWithPlugins()
    {
        StorageSystem storageSystem = new StorageSystem();
        PluginSystem pluginSystem = new PluginSystem(storageSystem);
        CharacterSystem characterSystem = new CharacterSystem(pluginSystem, storageSystem);
        
        characterSystem.CreateCharacter();
        Character pluginCharacter = characterSystem.GetAllCharacters().Last();
        pluginCharacter.Name = "插件机器人";
        // 使用一个实现了 IPlugin 的虚拟插件
        pluginCharacter.Plugins.Add(typeof(DummyPlugin)); 
        characterSystem.SaveCharacters();

        CharacterSystem reloadSystem = new CharacterSystem(pluginSystem, storageSystem);
        Character? loadedPluginChar = reloadSystem.GetAllCharacters().FirstOrDefault(c => c.Name == "插件机器人");

        Assert.NotNull(loadedPluginChar);
        Assert.Equal("插件机器人", loadedPluginChar.Name);
        Assert.Contains(loadedPluginChar.Plugins, p => p.Name == nameof(DummyPlugin));
    }
}

public class DummyPlugin : Plugin
{
}
