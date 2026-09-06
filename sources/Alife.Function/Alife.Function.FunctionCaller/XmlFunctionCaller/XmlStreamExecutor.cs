using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Alife.Function.FunctionCaller;

public class XmlExecutorContext : XmlContext
{
    public required IReadOnlyList<string> CallChain { get; init; }

    public string AboveContent { get; init; } = "";
    public string? AboveSeparator { get; init; }
    public string FullContent => AboveContent + Content;
}

public class XmlStreamExecutor : IAsyncDisposable
{
    /// <summary>
    /// 调用函数时出现的异常信息（函数可能不存在）
    /// </summary>
    public event Action<string, Exception>? Error;
    /// <summary>
    /// 接收意图调用的函数（函数可能不存在）
    /// </summary>
    public event Action<string, XmlContext>? Handling;

    public bool IsInactive =>
        commandChannel.Reader.TryPeek(out _) == false &&
        lastTask is null or { IsCompleted: true } || processingTokenSource.IsCancellationRequested;

    public void Feed(string text)
    {
        foreach (char ch in text)
            commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Feed, ch));
    }
    public void Flush()
    {
        commandChannel.Writer.TryWrite(new StreamCommand(CommandType.Flush));
    }
    public async Task CancelAndClearAsync()
    {
        while (commandChannel.Reader.TryRead(out _)) {}
        await handleTokenSource.CancelAsync();
        Flush();
        await WaitToInactive();
    }
    public async Task WaitToInactive(CancellationToken cancellationToken = default)
    {
        while (IsInactive == false)
        {
            await Task.Delay(100, cancellationToken);
        }
    }

    enum CommandType
    {
        Feed,
        Flush,
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
    Task? lastTask;
    CancellationTokenSource handleTokenSource = new();

    public XmlStreamExecutor(XmlStreamParser parser, XmlHandlerTable handler, string[]? sentenceBreakers = null,
        int minBreakingLength = 0)
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
                            // Console.Write(cmd.Data);
                            await (lastTask = parser.Feed(cmd.Data));
                            break;
                        case CommandType.Flush:
                            // Console.Write("[Flush]");
                            await (lastTask = parser.Flush(true));
                            ClearContentBuffer();
                            handleTokenSource = new CancellationTokenSource();
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) {}
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    async Task OnTagOpened()
    {
        if (aboveContentBuffer.Count < parser.TagStack.Count)
            aboveContentBuffer.Add(new StringBuilder());

        await FlushContentBuffer(skipTop: true);//有新的标签要进入，不能让新标签拿到老内容
        await HandleTag(CallMode.Opening);
    }

    async Task OnTagClosed()
    {
        await FlushContentBuffer();//即使没有触发分词也必须推送了，因为标签即将关闭
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
        return HandleXml(tagName, context);
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
                    return FlushContentBuffer(breaker);//提前推送一次content
            }
        }

        return Task.CompletedTask;
    }

    async Task FlushContentBuffer(string? separator = null, bool skipTop = false)
    {
        string content = contentBuffer.ToString();
        contentBuffer.Clear();
        if (content == string.Empty)
            return;

        for (int index = parser.TagStack.Count - (skipTop ? 2 : 1); index >= 0; index--)
        {
            string tagName = parser.TagStack[index];
            string aboveContent = aboveContentBuffer[index].ToString();
            IReadOnlyList<string> callChain = parser.TagStack.Take(index + 1).ToList();
            XmlExecutorContext context = new() {
                CallChain = callChain,
                CallMode = CallMode.Content,
                Parameters = parser.TagParameters,
                AboveContent = aboveContent,
                AboveSeparator = separator,
                Content = content,
            };

            await HandleXml(tagName, context);

            //获取调用后的内容，这可能被修改
            content = context.Content;
            if (content == "")
                break;//被彻底拦截

            //缓存内容
            aboveContentBuffer[index].Append(content);
        }
    }

    void ClearContentBuffer()
    {
        foreach (StringBuilder stringBuilder in aboveContentBuffer)
            stringBuilder.Clear();
        contentBuffer.Clear();
    }

    async Task HandleXml(string name, XmlContext tagContext)
    {
        try
        {
            Handling?.Invoke(name, tagContext);
            await handler.Handle(name, tagContext, handleTokenSource.Token);
        }
        catch (Exception e)
        {
            Error?.Invoke(name, e.InnerException ?? e);
        }
    }
}
