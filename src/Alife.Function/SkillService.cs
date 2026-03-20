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
你拥有使用和编写“技能”的功能。你的技能仓库位于：
{skillsPath}

## 如何发现技能：
**严禁凭空猜测技能脚本名。** 每当你想要执行任务时，请先使用 `os.listdir()` 或其他文件管理工具列出上述文件夹，根据**文件名（文件名通常包含功能描述和参数说明）**来选择合适的脚本。

## 关于系统缓存：
你的缓存文件（截图、抓取全文等）通常保存在 `%LOCALAPPDATA%\Alife\Cache` 中。技能脚本会返回这些文件的完整路径。

## 如何执行技能：
请使用你的 `Python` 工具执行代码。
例如：`import os; os.system('python ""xxx.py"" 参数值');`

## 技能进化：
如果你发现现有的技能脚本无法完成任务，你可以：
1. 直接在 `{skillsPath}` 下用 Python 编写一个新的 `.py` 脚本。
2. 确保文件名清晰描述了它的功能和需要的参数。
3. 之后你（或未来的你）就可以像使用其他技能一样调用它了。
");

        return Task.CompletedTask;
    }
}
