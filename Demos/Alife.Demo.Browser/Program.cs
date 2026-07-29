using Alife.Framework;
using Alife.Function.Browser;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(BrowserService).FullName!
    ]
};
await DemoSuite.Run(character);
