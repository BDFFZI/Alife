using Alife.Framework;
using Alife.Function.DeskPet;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(DeskPetService).FullName!
    ]
};
await DemoSuite.Run(character);
