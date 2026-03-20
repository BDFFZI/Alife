using Alife.Abstractions;
using Microsoft.SemanticKernel;

namespace Alife.OfficialPlugins;

[Plugin("使用技能", "让ai获得读写技能的功能，利用预先编写的技能脚本，可以实现复杂的任务需求。")]
public class SkillService : Plugin
{
    readonly StorageSystem storageSystem;

    public SkillService(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
    }

    public override Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        string skillsPath = $"{storageSystem.GetStoragePath()}/Skills";

        chatActivity.ChatBot.ChatHistory.AddSystemMessage($@"# {nameof(SkillService)}
你拥有使用和编写“技能”的功能。“技能”是通过python脚本，对特定功能的封装，能让你快速调用而不用从头造轮子。

## 当前技能

你的技能文件夹位于：
{skillsPath}
请通过python查看文件夹内容来获取已有技能。
目前根目录存在的技能有：
{string.Join('\n', Directory.GetFiles(skillsPath))}

## 使用说明

1. 每个技能都是一个可直接执行的python脚本，且都在文件名中写明了功能和可能的参数。
2. 使用时，直接用类似命令行的方式执行，如`import os; os.system('python ""xxx.py"" 参数值(如果需要的话)');`
3. 你也可以按照上述规则，在技能文件夹中制作自己的技能脚本。
");

        return Task.CompletedTask;
    }
}
