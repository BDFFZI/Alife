using Alife.Framework;
using Alife.Function.Python;
using Microsoft.Extensions.DependencyInjection;

var character = new Character {
    Name = "开发测试助手",
    Modules = [
        typeof(PythonService).FullName!
    ]
};

await DemoSuite.Run(character, provider => {
    StorageSystem storageSystem = provider.GetRequiredService<StorageSystem>();
    storageSystem.SetProperty("endpoint", "https://opencode.ai/zen/v1");
    storageSystem.SetProperty("apiKey", "sk-12cuQtqJ5gx71aL0NjwU5GYuBv1xG7BMOOAjyXw21e3EeuFhsLiibLVgN3HRhaRi");
    storageSystem.SetProperty("modelId", "deepseek-v4-flash-free");
});
