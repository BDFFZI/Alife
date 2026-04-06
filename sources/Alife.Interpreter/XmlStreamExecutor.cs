using System.Text;
using System.Threading.Channels;

namespace Alife.Interpreter;

public enum CallMode
{
    Opening,
    Closing,
    OneShot,
    Content,
}
public class XmlExecutorContext : XmlContext
{
    public required IReadOnlyList<string> CallChain { get; init; }
    public CallMode CallMode { get; init; }
    public string AboveContent { get; set; } = "";
    public string? AboveSeparator { get; set; }
    public string FullContent => AboveContent + Content;
}
public class XmlStreamExecutor : IAsyncDisposable
{
    public bool IsIdle => commandChannel.Reader.TryPeek(out _) == false;
    public void Feed(string text)
    {
        foreach (char ch in text)
            commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Feed, ch));
    }
    public void Flush()
    {
        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Flush));
    }
    public void Reset()
    {
        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Reset));
    }

    enum CommandType
    {
        Feed,
        Flush,
        Reset
    }

    record struct StreamCommand(CommandType Type, char Data = '\0');

    readonly XmlStreamParser parser;
    readonly XmlHandlerTable handler;
    readonly string[] sentenceBreakers;
    readonly int minBreakingLength;
    readonly CancellationTokenSource processingTokenSource;

    readonly Channel<StreamCommand> commandChannel = Channel.CreateUnbounded<StreamCommand>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false,
    });
    readonly List<StringBuilder> aboveContentBuffer = new();
    readonly StringBuilder contentBuffer = new();

    public XmlStreamExecutor(XmlStreamParser parser, XmlHandlerTable handler, string[]? sentenceBreakers = null, int minBreakingLength = 0)
    {
        this.parser = parser;
        this.handler = handler;
        this.sentenceBreakers = sentenceBreakers ?? [",", ".", "!", "?", "，", "。", "！", "？"];
        this.minBreakingLength = minBreakingLength;

        this.parser.TagOpened = OnTagOpened;
        this.parser.TagShotted = OnTagShotted;
        this.parser.TagClosed = OnTagClosed;
        this.parser.ContentGot = OnContentGot;

        processingTokenSource = new CancellationTokenSource();
        LoopProcessInput(processingTokenSource.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await processingTokenSource.CancelAsync();
    }

    async void LoopProcessInput(CancellationToken cancellationToken = default)
    {
        try
        {
            while (await commandChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (commandChannel.Reader.TryRead(out StreamCommand cmd))
                {
                    switch (cmd.Type)
                    {
                        case CommandType.Feed:
                            await parser.Feed(cmd.Data);
                            break;
                        case CommandType.Flush:
                            await parser.Flush();
                            ClearContentBuffer();
                            break;
                        case CommandType.Reset:
                            parser.Reset();
                            ClearContentBuffer();
                            break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    Task OnTagOpened()
    {
        if (aboveContentBuffer.Count < parser.TagStack.Count)
            aboveContentBuffer.Add(new StringBuilder());
        return HandleTag(CallMode.Opening);
    }

    async Task OnTagClosed()
    {
        if (contentBuffer.Length != 0)
            await FlushContentBuffer(); //即使没有触发分词也必须推送了，因为标签即将关闭
        await HandleTag(CallMode.Closing);
        aboveContentBuffer[parser.TagStack.Count - 1].Clear();
    }

    Task OnTagShotted()
    {
        if (aboveContentBuffer.Count < parser.TagStack.Count)
            aboveContentBuffer.Add(new StringBuilder());
        return HandleTag(CallMode.OneShot);
    }

    Task HandleTag(CallMode callMode)
    {
        string tagName = parser.TagStack.Last();
        string aboveContent = aboveContentBuffer[parser.TagStack.Count - 1].ToString();
        XmlExecutorContext context = new() {
            CallChain = parser.TagStack,
            CallMode = callMode,
            Parameters = parser.TagParameters,
            AboveContent = aboveContent,
            AboveSeparator = null,
            Content = "",
        };
        return handler.TryHandle(tagName, context);
    }

    /// <summary>
    /// 接收缓存字符，同时检测自动分词，如果触发分词，则提前推送一次content
    /// </summary>
    Task OnContentGot(char ch)
    {
        contentBuffer.Append(ch);

        if (contentBuffer.Length >= minBreakingLength)
        {
            string content = contentBuffer.ToString();
            foreach (string breaker in sentenceBreakers)
            {
                if (content.EndsWith(breaker))
                    return FlushContentBuffer(breaker); //提前推送一次content
            }
        }

        return Task.CompletedTask;
    }

    async Task FlushContentBuffer(string? separator = null)
    {
        string content = contentBuffer.ToString();
        contentBuffer.Clear();

        for (int index = parser.TagStack.Count - 1; index >= 0; index--)
        {
            string tagName = parser.TagStack[index];
            string aboveContent = aboveContentBuffer[index].ToString();
            XmlExecutorContext context = new() {
                CallChain = parser.TagStack,
                CallMode = CallMode.Content,
                Parameters = parser.TagParameters,
                AboveContent = aboveContent,
                AboveSeparator = separator,
                Content = content,
            };

            await handler.TryHandle(tagName, context);

            //获取调用后的内容，这可能被修改
            content = context.Content;
            if (content == "")
                break; //被彻底拦截
        }

        //缓存内容
        for (int i = 0; i < parser.TagStack.Count; i++)
            aboveContentBuffer[i].Append(content);
    }
    void ClearContentBuffer()
    {
        foreach (StringBuilder stringBuilder in aboveContentBuffer)
            stringBuilder.Clear();
        contentBuffer.Clear();
    }
}
