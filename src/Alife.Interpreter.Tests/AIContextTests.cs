using Alife.Interpreter;
using Xunit;
using Xunit.Abstractions;
using System.Text;

namespace Alife.Interpreter.Tests;

public class AIContextTests
{
    private readonly ITestOutputHelper _output;

    public AIContextTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public enum TestTone { Happy, Dismay }

    public class TestHandlers
    {
        [XmlHandler("video", "视频播放")]
        public void VideoHandler(float speed, [XmlTagContent] string content, TestTone tone = TestTone.Happy) { }

        [XmlHandler("speech", "语音合成")]
        public void SpeechHandler(string content) { }
    }

    [Fact]
    public void Metadata_Should_ExtractCorrectParameters()
    {
        // Arrange
        var compiler = new XmlHandlerCompiler();
        compiler.Register(new TestHandlers());

        // Act
        var table = compiler.Compile();

        // Assert
        Assert.True(table.TagParameters.ContainsKey("video"));
        var videoParams = table.TagParameters["video"];
        
        // speed: float, required
        var speedParam = videoParams.Find(p => p.Name == "speed");
        Assert.NotNull(speedParam);
        Assert.Equal("float", speedParam.Type);
        Assert.False(speedParam.IsOptional);

        // tone: enum, optional
        var toneParam = videoParams.Find(p => p.Name == "tone");
        Assert.NotNull(toneParam);
        Assert.Contains("enum", toneParam.Type);
        Assert.True(toneParam.IsOptional);
        Assert.NotNull(toneParam.PossibleValues);
        Assert.Equal(2, toneParam.PossibleValues.Length);
    }

    [Fact]
    public void Metadata_Should_ExtractCorrectDescriptions()
    {
        // Arrange
        var compiler = new XmlHandlerCompiler();
        compiler.Register(new TestHandlers());

        // Act
        var table = compiler.Compile();

        // Assert
        Assert.Equal("视频播放", table.Descriptions["video"]);
        Assert.Equal("语音合成", table.Descriptions["speech"]);
    }

    [Fact]
    public void DisplayDocumentation_ForUserVerification()
    {
        // Arrange
        var compiler = new XmlHandlerCompiler();
        compiler.Register(new TestHandlers());
        var table = compiler.Compile();

        // Act
        string message = table.GenerateDocumentation();

        // Assert
        _output.WriteLine("Generated AI System Message (Full Effect):");
        _output.WriteLine("==========================================");
        _output.WriteLine(message);
        _output.WriteLine("==========================================");
        
        // 同时写入文件方便查阅
        System.IO.File.WriteAllText("full_system_message.txt", message);
        Assert.NotEmpty(message);
    }
}
