using Alife.Function.FunctionCaller;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

[TestFixture]
public class XmlStreamParserTests
{
    [Test]
    public async Task TestXmlStreamParser()
    {
        XmlStreamParser parser = new XmlStreamParser();
        StringBuilder stringBuilder = new StringBuilder();
        StringBuilder output = new StringBuilder();

        void Log(string tag, IReadOnlyDictionary<string, string>? dictionary = null)
        {
            output.AppendLine("======");
            output.AppendLine($"调用：{string.Join('-', parser.TagStack.Reverse())}");
            output.AppendLine($"区间：{tag}");
            if (dictionary != null)
                output.AppendLine($"参数：\n{JsonConvert.SerializeObject(dictionary, Formatting.Indented)}");
        }

        parser.TagOpened = () =>
        {
            Log("打开" + parser.TagStack.Last(), parser.TagParameters);
            stringBuilder.Clear();
            return Task.CompletedTask;
        };
        parser.TagClosed = () =>
        {
            Log("关闭" + parser.TagStack.Last());
            output.AppendLine("内容：" + stringBuilder);
            stringBuilder.Clear();
            return Task.CompletedTask;
        };
        parser.Error += (tag, ex) =>
        {
            Log(tag);
            output.AppendLine(ex.Message);
        };
        parser.TagShotted = () =>
        {
            Log("一次" + parser.TagStack.Last(), parser.TagParameters);
            return Task.CompletedTask;
        };
        parser.ContentGot = c =>
        {
            stringBuilder.Append(c);
            return Task.CompletedTask;
        };

        await parser.Feed(@"
<response>
    <content type=""text"" lang=""zh-CN"">
        <!--的231<>dasd<-->
        你好！这是一个完全正常的 XML 示例数据。
        它可以用于测试解析器在遇到标准语法时的表现。
    </content>
    
    <userProfile id=""1001"" role=""admin"">
        <name>Alice< / name>
        <standard >使用标准实体：&lt; &gt; &amp;</standard>
        <preferences>
            < theme>dark</theme>
            <notifications enabled=""true"" / >
        </preferences>
</response>

最后再拖拽一段没有根节点的游离文本。
");
        await parser.Flush();

        string actual = Regex.Replace(output.ToString().Replace("\r\n", "\n"), @"\n[ \t]+\n", "\n\n");
        string expected = $@"======
调用：response
区间：打开response
参数：
{{}}
======
调用：content-response
区间：打开content
参数：
{{
  ""type"": ""text"",
  ""lang"": ""zh-CN""
}}
======
调用：content-response
区间：关闭content
内容：

        你好！这是一个完全正常的 XML 示例数据。
        它可以用于测试解析器在遇到标准语法时的表现。

======
调用：userprofile-response
区间：打开userprofile
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin""
}}
======
调用：name-userprofile-response
区间：打开name
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin""
}}
======
调用：name-userprofile-response
区间：关闭name
内容：Alice
======
调用：standard-userprofile-response
区间：打开standard
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin""
}}
======
调用：standard-userprofile-response
区间：关闭standard
内容：使用标准实体：< > &
======
调用：preferences-userprofile-response
区间：打开preferences
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin""
}}
======
调用：theme-preferences-userprofile-response
区间：打开theme
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin""
}}
======
调用：theme-preferences-userprofile-response
区间：关闭theme
内容：dark
======
调用：notifications-preferences-userprofile-response
区间：一次notifications
参数：
{{
  ""id"": ""1001"",
  ""role"": ""admin"",
  ""enabled"": ""true""
}}
======
调用：preferences-userprofile-response
区间：关闭preferences
内容：

        
======
调用：userprofile-response
区间：userprofile
检测到无效的孤儿开标签：userprofile
======
调用：userprofile-response
区间：关闭userprofile
内容：

======
调用：response
区间：关闭response
内容：
".Replace("\r\n", "\n");

        Console.WriteLine(actual);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task TestXmlStreamParser2()
    {
        TaskCompletionSource tcs = new TaskCompletionSource();

        XmlStreamParser parser = new XmlStreamParser();
        parser.TagShotted = () =>
        {
            try
            {
                Assert.That(parser.TagParameters["arg1"], Is.EqualTo("a&b"));
                Assert.That(parser.TagParameters["arg2"], Is.EqualTo("a&b"));
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }

            return Task.CompletedTask;
        };
        await parser.Feed(@"<send arg1=""a&amp;b"" arg2=""a&b""/>你好");
        await tcs.Task;
    }
}