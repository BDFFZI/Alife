using Alife.Framework;
using Alife.Function.Speech;
using Alife.Function.Speech.VITS;

Console.WriteLine(typeof(VitsSpeechModel));

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(SpeechService).FullName!
    ]
};

await DemoSuite.Run(character);
