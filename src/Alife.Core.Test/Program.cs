using Alife;
using Newtonsoft.Json;

Console.WriteLine("=== Alife.Core.Test: 开始模拟用户流程 ===");

// 1. 初始化 StorageSystem
StorageSystem storageSystem = new StorageSystem();
Console.WriteLine($"[1] StorageSystem 初始化完成。存储路径: {AppContext.BaseDirectory}Storage");

// 2. 初始化 CharacterSystem
CharacterSystem characterSystem = new CharacterSystem(storageSystem);
int initialCount = characterSystem.GetAllCharacters().Count();
Console.WriteLine($"[2] CharacterSystem 初始化完成。当前角色数量: {initialCount}");

// 3. 创建新角色
Console.WriteLine("[3] 正在创建新角色...");
characterSystem.CreateCharacter();
int afterCreateCount = characterSystem.GetAllCharacters().Count();
Console.WriteLine($"[3] 角色已创建。当前角色数量: {afterCreateCount}");

if (afterCreateCount != initialCount + 1)
{
    Console.WriteLine("错误: 角色数量未增加！");
}

// 4. 保存数据
Console.WriteLine("[4] 正在保存数据...");
characterSystem.SaveData();

// 5. 验证持久化
Console.WriteLine("[5] 正在验证持久化 (模拟重启)...");
CharacterSystem newCharacterSystem = new CharacterSystem(storageSystem);
int finalCount = newCharacterSystem.GetAllCharacters().Count();
Console.WriteLine($"[5] 重启后角色数量: {finalCount}");

// 6. 验证带插件的角色
Console.WriteLine("\n[6] 正在验证带插件的角色...");
characterSystem.CreateCharacter();
Character pluginCharacter = characterSystem.GetAllCharacters().Last();
pluginCharacter.Name = "插件机器人";
// 尝试添加一个系统类型作为插件
pluginCharacter.Plugins.Add(typeof(string)); 
characterSystem.SaveData();

Console.WriteLine("[6] 已保存带插件的角色。正在重新加载验证...");
CharacterSystem reloadSystem = new CharacterSystem(storageSystem);
Character? loadedPluginChar = reloadSystem.GetAllCharacters().FirstOrDefault(c => c.Name == "插件机器人");

if (loadedPluginChar != null)
{
    Console.WriteLine($"[6] 成功找到角色: {loadedPluginChar.Name}");
    if (loadedPluginChar.Plugins.Contains(typeof(string)))
    {
        Console.WriteLine("=== [6] 测试通过: Plugins (HashSet<Type>) 序列化成功 ===");
    }
    else
    {
        Console.WriteLine("!!! [6] 测试失败: Plugins 丢失或反序列化失败 !!!");
        Console.WriteLine($"当前插件数量: {loadedPluginChar.Plugins.Count}");
    }
}
else
{
    Console.WriteLine("!!! [6] 测试失败: 找不到角色 '插件机器人' !!!");
}

Console.WriteLine("\n=== 测试结束 ===");
