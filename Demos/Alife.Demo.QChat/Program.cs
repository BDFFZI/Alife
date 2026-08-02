using Alife.Framework;
using Alife.Function.QChat;
using Microsoft.Extensions.DependencyInjection;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(QChatService).FullName!
    ]
};

await DemoSuite.Run(character, provider => {
    ConfigurationSystem configurationSystem = provider.GetRequiredService<ConfigurationSystem>();
    configurationSystem.SetConfiguration(typeof(QChatService), new QChatConfig {
        Url = "ws://127.0.0.1:3001",
        Token = "",
        OwnerId = 1330958515L,
        BotId = 3148702330L,
    },character.StorageKey);
});
