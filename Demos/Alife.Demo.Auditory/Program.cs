using Alife.Framework;
using Alife.Function.Auditory;
using Alife.Function.Auditory.SenseVoice;

Console.WriteLine(typeof(SenseVoiceAuditoryModel));

var character = new Character {
    Name = "听觉验证助手",
    Description = "用于验证听觉服务功能的开发测试角色。",
    Modules = [
        typeof(AuditoryService).FullName!,
        typeof(AudioRecognitionService).FullName!,
        typeof(SenseVoiceAuditoryModel).FullName!,
    ]
};

await DemoSuite.Run(character);
