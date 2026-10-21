using Alife.Framework;
using Alife.Function.Python;
using Microsoft.Extensions.DependencyInjection;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(ExecutionService).FullName!
    ]
};

await DemoSuite.Run(character);