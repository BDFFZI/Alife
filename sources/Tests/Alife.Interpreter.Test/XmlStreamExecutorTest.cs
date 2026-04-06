using Alife.Interpreter;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Interpreter.Test;

[TestFixture]
public class XmlStreamExecutorTest
{
    private class TestHandler
    {
        public List<string> Logs = new();

        [XmlFunction("test")]
        public void Test(XmlExecutorContext context)
        {
            Logs.Add($"test:{context.CallMode}:{context.Content}");
        }

        [XmlFunction("modify")]
        public void Modify(XmlExecutorContext context, ref string content)
        {
            Logs.Add($"modify:{context.CallMode}:{content}");
            if (context.CallMode == CallMode.Content)
            {
                content = $"[{content}]";
            }
        }

        [XmlFunction("oneshot")]
        public void OneShot(XmlExecutorContext context)
        {
            Logs.Add($"oneshot:{context.CallMode}");
        }
    }

    [Test]
    public async Task TestExecutorBasic()
    {
        var handler = new TestHandler();
        var table = new XmlHandlerTable();
        table.Register(handler);

        var parser = new XmlStreamParser();
        // Set minBreakingLength to 1 to force split on every separator
        await using var executor = new XmlStreamExecutor(parser, table, [".", "!"], 1);

        executor.Feed("<test>Hello. World!</test>");
        executor.Flush();

        while (executor.IsIdle == false)
        {
            await Task.Delay(200);
        }

        // Logs should contain Content calls for each segment and a Closing call
        Assert.That(handler.Logs, Has.Member("test:Content:Hello."));
        Assert.That(handler.Logs, Has.Member("test:Content: World!"));
        Assert.That(handler.Logs, Has.Member("test:Closing:"));
    }

    [Test]
    public async Task TestOneShot()
    {
        var handler = new TestHandler();
        var table = new XmlHandlerTable();
        table.Register(handler);

        var parser = new XmlStreamParser();
        await using var executor = new XmlStreamExecutor(parser, table, [], 100);

        executor.Feed("<oneshot />");
        executor.Flush();

        await Task.Delay(200);

        Assert.That(handler.Logs, Has.Member("oneshot:OneShot"));
    }

    [Test]
    public async Task TestNestedTags()
    {
        var handler = new TestHandler();
        var table = new XmlHandlerTable();
        table.Register(handler);

        var parser = new XmlStreamParser();
        await using var executor = new XmlStreamExecutor(parser, table, [], 100);

        executor.Feed("<test><test>nested</test></test>");
        executor.Flush();

        await Task.Delay(200);

        // Outer tag gets "nested" too because of AboveContent logic
        // But the immediate call for the inner tag will be Content:nested, Closing:
        // And the outer tag will eventually get Content:nested, Closing:

        Assert.That(handler.Logs, Has.Count.AtLeast(4));
    }
}
