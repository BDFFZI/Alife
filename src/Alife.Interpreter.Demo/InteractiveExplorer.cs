using System.Text;
using Alife;
using Alife.Abstractions;
using Alife.Modules.Context;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;
using Alife.Test;
using Microsoft.SemanticKernel;

namespace Alife.Interpreter.Demo;

public class InteractiveExplorer
{
    public static async Task RunAsync()
    {
        Terminal.Log("========================================", ConsoleColor.Magenta);
        Terminal.Log("   Alife Interpreter 协议集成验证 Demo", ConsoleColor.Magenta);
        Terminal.Log("========================================", ConsoleColor.Magenta);

        // 1. 配置通用工具人角色
        var character = new Character {
            ID = "InterpreterMao",
            Name = "真央",
            Prompt = "你是一个桌宠助理真央喵！你非常活泼，喜欢模仿猫娘（说话带喵）。\n" +
                     "这个演示环境专门用于测试你的 XML 协议理解能力。你可以随时尝试使用 <speak>, <motion>, <expression> 等标签喵！",
            Plugins = new HashSet<Type> {
                typeof(InterpreterService),
                typeof(OpenAIChatService),
                typeof(ChatService),
                typeof(DialogContext),
                typeof(PetService) 
            }
        };

        // 2. 初始化套件
        var suite = await DemoSuite.InitializeAsync(character);

        Terminal.LogInfo("提示：此环境用于测试 AI 如何自主决定调用哪些工具。输入文字开启对话喵！");
        Terminal.Log("--------------------------------------------------\n", ConsoleColor.Gray);

        // 3. 运行交互循环
        await suite.RunAsync();

        Terminal.Log("演示结束，再见喵！", ConsoleColor.Magenta);
    }
}
