using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alife.Foundation;

namespace Alife.Function.FunctionCaller;

public class XmlExecutorContext : XmlContext
{
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
    public event Action<string>? Waiting;

    public bool IsFeeding => fedCompletionSource.Task.IsCompleted;

    public void StartFeeding()
    {
        fedCompletionSource = new TaskCompletionSource();
    }

    /// <summary>
    /// 向解析队列中推入字符并可能的触发事件
    /// </summary>
    /// <param name="text"></param>
    public void Feed(string text)
    {
        foreach (char ch in text)
            commandChannel.Add(new StreamCommand(CommandType.Feed, ch));
    }

    /// <summary>
    /// 向解析队列中推入flash信号，使解析器在收到后立即结算缓存的所有字符并触发相应事件
    /// </summary>
    public Task EndFeeding()
    {
        commandChannel.Add(new StreamCommand(CommandType.Flush));
        return fedCompletionSource.Task;
    }

    /// <summary>
    /// 排空还未解析的字符，并对已触发的事件发送取消信号，然后等待解析队列完全空出
    /// </summary>
    public async Task CancelFeeding()
    {
        //取消未进入的
        while (commandChannel.TryTake(out _)) {}
        //取消已进入的
        await handleTokenSource.CancelAsync();
        handleTokenSource = new CancellationTokenSource();
        //等待Flush完成
        await EndFeeding();
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
    readonly BlockingCollection<StreamCommand> commandChannel = new();
    TaskCompletionSource fedCompletionSource = new();

    readonly List<StringBuilder> aboveContentBuffer = new();
    readonly StringBuilder contentBuffer = new();
    CancellationTokenSource handleTokenSource = new();
    readonly Dictionary<string, Task> parallelTasks = new(StringComparer.OrdinalIgnoreCase);

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

    void LoopProcessInput(CancellationToken cancellationToken = default)
    {
        Task.Run(async () => {
            try
            {
                while (cancellationToken.IsCancellationRequested == false)
                {
                    StreamCommand cmd = commandChannel.Take(cancellationToken);
                    switch (cmd.Type)
                    {
                        case CommandType.Feed:
                            await parser.Feed(cmd.Data);
                            break;
                        case CommandType.Flush:
                            await OnCommandFlush();
                            break;
                    }
                }
            }
            catch (OperationCanceledException) {}
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }, cancellationToken);
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
        return HandleXml(tagName, context, handleTokenSource.Token);
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

        ImmutableList<string> callChain = parser.TagStack;
        if (skipTop) callChain = callChain.RemoveAt(callChain.Count - 1);

        for (int index = parser.TagStack.Count - (skipTop ? 2 : 1); index >= 0; index--)
        {
            string tagName = parser.TagStack[index];
            string aboveContent = aboveContentBuffer[index].ToString();
            XmlExecutorContext context = new() {
                CallChain = callChain,
                CallMode = CallMode.Content,
                Parameters = parser.TagParameters,
                AboveContent = aboveContent,
                AboveSeparator = separator,
                Content = content,
            };

            await HandleXml(tagName, context, handleTokenSource.Token);

            //获取调用后的内容，这可能被修改
            content = context.Content;
            if (content == "")
                break;//被彻底拦截

            //缓存内容
            aboveContentBuffer[index].Append(content);
            //隔离调用链
            callChain = callChain.RemoveAt(callChain.Count - 1);
        }
    }

    void ClearContentBuffer()
    {
        foreach (StringBuilder stringBuilder in aboveContentBuffer)
            stringBuilder.Clear();
        contentBuffer.Clear();
    }

    async Task HandleXml(string name, XmlContext tagContext, CancellationToken cancellationToken)
    {
        Handling?.Invoke(name, tagContext);

        bool isParallel = tagContext.Parameters.TryGetValue("#parallel", out string? parallel) ? parallel == "true" : handler.IsParallelFunction(name);
        bool isBackground = tagContext.Parameters.TryGetValue("#background", out string? background) && background == "true";

        if (isParallel)
        {
            ContinueParallelTask(name);
        }
        else
        {
            int index;
            for (index = tagContext.CallChain.Count - 1; index >= 0; index--)
            {
                string chain = tagContext.CallChain[index];
                if (parallelTasks.ContainsKey(chain))
                {
                    ContinueParallelTask(chain);
                    break;
                }
            }

            if (index < 0)
            {
                Waiting?.Invoke(name);
                try
                {
                    await handler.Handle(name, tagContext, cancellationToken);
                }
                catch (Exception e)
                {
                    Error?.Invoke(name, e.InnerException ?? e);
                }
            }
        }

        void ContinueParallelTask(string source)
        {
            if (isBackground)
            {
                Task task = handler.Handle(name, tagContext, processingTokenSource.Token);
                task.ContinueWith(task => {
                    if (task.IsFaulted)
                        Error?.Invoke(name, task.Exception);
                }, processingTokenSource.Token);
            }
            else
            {
                lock (parallelTasks)
                {
                    if (parallelTasks.ContainsKey(source) == false)
                    {
                        parallelTasks[name] = handler.Handle(name, tagContext, cancellationToken);
                    }
                    else
                    {
                        parallelTasks[name] = parallelTasks[source].ContinueWith(async _ => {
                            await handler.Handle(name, tagContext, cancellationToken);
                        }, CancellationToken.None).Unwrap();
                    }
                }
            }
        }
    }

    async Task OnCommandFlush()
    {
        await parser.Flush(true);

        foreach ((string name, Task task) in parallelTasks)
        {
            Waiting?.Invoke(name);
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Error?.Invoke(name, e.InnerException ?? e);
            }
        }

        parallelTasks.Clear();
        ClearContentBuffer();

        if (fedCompletionSource.Task.IsCompleted == false)
            fedCompletionSource.SetResult();
    }
}
