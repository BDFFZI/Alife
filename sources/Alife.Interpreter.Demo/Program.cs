using System.Text;
using Newtonsoft.Json;

public class Program
{
    static async Task Main(string[] args)
    {
        await TestXmlStreamParser();
    }
    
    static Task TestXmlStreamParser()
    {
        XmlStreamParser parser = new XmlStreamParser();
        StringBuilder stringBuilder = new StringBuilder();
        StringBuilder output = new StringBuilder();

        parser.TagOpened += (tag, dictionary) => {
            Log("打开" + tag, dictionary);
            stringBuilder.Clear();
        };
        parser.TagClosed += tag => {
            Log("关闭" + tag);
            output.AppendLine("内容：" + stringBuilder);
            stringBuilder.Clear();
        };
        parser.TagShotted += (tag, dictionary) => {
            Log("一次" + tag, dictionary);
        };
        parser.ContentGot += c => {
            stringBuilder.Append(c);
        };

        parser.Feed(@"
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
        parser.Flush();
        output.ToString()

        while (true)
        {
            string input = Console.ReadLine()!;
            parser.Feed(input.Trim());
        }

        void Log(string tag, IReadOnlyDictionary<string, string>? dictionary = null)
        {
            output.AppendLine("======");
            output.AppendLine($"调用：{string.Join('-', parser.TagStack)}");
            output.AppendLine($"区间：{tag}");
            if (dictionary != null)
                output.AppendLine($"参数：\n{JsonConvert.SerializeObject(dictionary, Formatting.Indented)}");
        }
    }
}
