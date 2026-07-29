using Alife.Framework;
using Alife.Function.Vision;
using Alife.Function.Vision.MiniCPM;

Console.WriteLine(typeof(MiniCPMVisionModel));

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(VisionService).FullName!
    ]
};

await DemoSuite.Run(character);
