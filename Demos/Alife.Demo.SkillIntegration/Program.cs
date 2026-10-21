using Alife.Framework;
using Alife.Function.Skill;
using Alife.Function.Python;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(SkillService).FullName!,
        typeof(ExecutionService).FullName!,
    ]
};

await DemoSuite.Run(character);
