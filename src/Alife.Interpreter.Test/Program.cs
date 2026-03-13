using Alife.Interpreter;

// 1. 初始化
var table = new XmlHandlerCompiler()
    .Register(new MyHandlers())
    .Compile();

var parser = new XmlStreamParser();
// 指定断句符号（可选）
var breakers = new[] { ',', '.', '!', '?', '，', '。', '！', '？' };
var executor = new XmlStreamExecutor(parser, table, breakers);

// 2. 运行
Console.WriteLine("Alife Ready. Input XML (e.g. <chat>hello<think>processing</think></chat>) or 'quit':");

while (true)
{
    Console.Write("> ");
    string? line = Console.ReadLine();
    if (string.IsNullOrEmpty(line) || line.ToLower() == "quit") break;

    foreach (char ch in line) await executor.Feed(ch);
    Console.WriteLine();
}

// 3. 处理程序
class MyHandlers
{
    [XmlHandler("think")]
    public Task OnThink(XmlTagContext ctx, [XmlTagContent] ref string content)
    {
        string triggerDisplay = ctx.Trigger.HasValue ? ctx.Trigger.Value.ToString() : "null";
        Console.WriteLine($"[think] trigger: {triggerDisplay}, input: {content}");
        if (content == "思考中" || content == "processing") content = "主人";
        return Task.Run(async () => {
            await Task.Delay(1000);
            Console.WriteLine("[think] async done");
        });
    }

    [XmlHandler("chat")]
    public void OnChat(string content)
    {
        Console.WriteLine($"[chat] output: {content}");
    }
}
