using Alife.Interpreter;
using Xunit;

namespace Alife.Interpreter.Tests;

public class XmlStreamExecutorTests
{
    [Fact]
    public async Task TestFeedAndExecution()
    {
        var handler = new MyTestHandlers();
        var table = new XmlHandlerCompiler()
            .Register(handler)
            .Compile();

        var parser = new XmlStreamParser();
        var executor = new XmlStreamExecutor(parser, table);

        executor.Feed("<test>Hello World!</test>");
        await Task.Delay(200);

        Assert.Equal("Hello World!", handler.LastContent);
    }

    [Fact]
    public async Task TestReset()
    {
        var handler = new MyTestHandlers();
        var table = new XmlHandlerCompiler()
            .Register(handler)
            .Compile();

        var parser = new XmlStreamParser();
        var executor = new XmlStreamExecutor(parser, table);

        executor.Feed("<test>Incomplete");
        await Task.Delay(50);
        executor.Reset();

        executor.Feed("<test>Valid</test>");
        await Task.Delay(200);

        Assert.Equal("Valid", handler.LastContent);
    }

    class MyTestHandlers
    {
        public string? LastContent { get; private set; }

        [XmlHandler("test")]
        public Task OnTest([XmlTagContent] string content)
        {
            LastContent = content;
            return Task.CompletedTask;
        }
    }
}
