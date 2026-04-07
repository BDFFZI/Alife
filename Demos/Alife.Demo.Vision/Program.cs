using Alife.Basic;
using Alife.Framework;
using Alife.Implement;


class Program
{
    static async Task Main(string[] args)
    {
        // 1. 定义角色：具有视觉能力的“真央”
        var character = new Character
        {
            ID = "VisionMao",
            Name = "真央",
            Prompt = "你叫真央，是一个拥有视觉能力的 AI 助手。你可以看到主人的屏幕内容，也可以分析指定的图片。\n" +
                      "当你需要看屏幕时，请使用 <look_screen> 标签（可附带你的问题，如：<look_screen>屏幕上有什么？</look_screen>）。\n" +
                      "当你需要分析本地图片时，请使用 <look_image path=\"图片完整路径\"> 标签。\n" +
                      "你的回答应该亲切自然，经常称呼用户为“主人”，并使用“喵”作为句尾。\n" +
                      "示例：\n" +
                      "主人问：帮我看看屏幕上在运行什么？\n" +
                      "你答：好的喵！主人请稍候，我这就来看看屏幕喵！<look_screen>描述一下屏幕上的主要窗口内容</look_screen>",
            Plugins = new HashSet<Type> {
                typeof(OpenAIChatService),   // 大模型对话支持
                typeof(InterpreterService),   // 标签解析支持
                typeof(VisionService),       // 视觉服务插件
            }
        };

        // 2. 初始化 Demo 套件
        var suite = await DemoSuite.InitializeAsync(character);

        Terminal.LogInfo("提示：视觉 Demo 已重构为大模型驱动模式。");
        Terminal.LogInfo("你可以直接对真央说：“帮我看看屏幕”、“这张图片里有什么：C:\\path\\to\\image.jpg”等。");
        Terminal.Log("--------------------------------------------------\n", ConsoleColor.Gray);

        // 3. 运行套件循环
        try
        {
            await suite.RunAsync();
        }
        catch (Exception ex)
        {
            Terminal.LogError($"Demo 运行出错：{ex.Message}");
        }

        Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
    }
}
