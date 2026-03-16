using System.Diagnostics;
using System.Text;
using Alife;
using Alife.OfficialPlugins;
using Alife.Plugins.Official.Implement;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

await AIPythonTest();

async Task AIPythonTest()
{
    var character = new Character {
        ID = "PetDemoMao",
        Name = "真央",
        Prompt = "你是一个桌宠，名叫真央。你非常活泼，喜欢用猫娘语说话（每句话带喵）。" +
                 "你可以通过控制桌宠应用来表达情感。",
        Plugins = new HashSet<Type> {
            typeof(OpenAIChatService),
            typeof(ChatWindow),
            typeof(InterpreterService),
            typeof(PythonService),
        }
    };

    var storageSystem = new StorageSystem();
    var configSystem = new ConfigurationSystem(storageSystem);
    ChatActivity chatActivity = await ChatActivity.Create(
        character, configSystem, appendServices: [
            storageSystem
        ]);
    chatActivity.ChatBot.ChatSent += message => Console.WriteLine($"[对话开始({message})]");
    chatActivity.ChatBot.ChatReceived += content => Console.Write(content);
    chatActivity.ChatBot.ChatOver += () => Console.WriteLine("[对话结束]");

    while (true)
    {
        Console.Write("> ");
        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "exit")
            break;

        await chatActivity.ChatBot.ChatAsync(input);
    }
}

void PythonTest()
{
    Process process = new Process();
    process.StartInfo = new ProcessStartInfo {
        FileName = "python",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
        Environment = { { "PYTHONIOENCODING", "utf-8" }, { "PYTHONUTF8", "1" } },
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        StandardInputEncoding = Encoding.UTF8,
    };
    process.Start();

    process.StandardInput.Write(@"print(""系统信息："")
print(f""Python版本: {sys.version}"")
print(f""操作系统: {platform.system()} {platform.release()}"")
print(f""当前时间: {datetime.datetime.now()}"")
print(f""随机数测试: {random.randint(1,100)}"")
print(f""数学计算: sin(π/6) = {math.sin(math.pi/6)}"")");
    process.StandardInput.Close();

    string result = process.StandardOutput.ReadToEnd();
    Console.WriteLine(result);
}
