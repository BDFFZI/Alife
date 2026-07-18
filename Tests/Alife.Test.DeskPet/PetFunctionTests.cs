using Alife.Function.DeskPet;
using Alife.Platform;
using System.IO;
using System.Windows;

namespace Alife.Test.DeskPet;

/// <summary>
/// 桌宠功能集成测试：采用人工互动验证模式
/// </summary>
[TestFixture]
public class PetFunctionTests
{
    [Test, Order(1)]
    public void TestBubble()
    {
        server.ShowBubble("Hello World！喵~");
        AskUser("真央是否显示了 “Hello World” 气泡？");
        server.HideBubble();
    }

    [Test, Order(2)]
    public void TestExpression()
    {
        server.PlayExpression("繁星眼");
        AskUser("真央的表情是否变成了星星眼（繁星眼）？");

        server.PlayExpression("微笑");
    }

    [Test, Order(3)]
    public void TestMotion()
    {
        server.PlayMotion("TapBody", 2); // 点头
        AskUser("真央是否播放了一个动作（例如点头）？");
    }

    [Test, Order(4)]
    public async Task TestPositionAndMove()
    {
        (double x, double y) = await server.GetPositionAsync();
        Assert.That(x, Is.GreaterThanOrEqualTo(0));
        Assert.That(y, Is.GreaterThanOrEqualTo(0));

        await server.MoveAsync(100, 100, 1000);

        AskUser("真央是否平滑地向右下方移动了 100 像素？");
    }

    [Test, Order(5)]
    public void TestMouseInteractions()
    {
        recordedInteractions.Clear();
        MessageBox.Show(
            "测试 [鼠标交互]: \n1. 请快速连击 (mouse_combo)\n2. 请绕着真央转 6 圈 (mouse_shake)\n\n完成后点击确定。",
            "人工指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
        
        Assert.That(recordedInteractions, Has.Some.StartWith("桌宠被连续触摸"), "未检测到鼠标连击");
        Assert.That(recordedInteractions, Does.Contain("鼠标在快速转圈"), "未检测到围绕转圈");
    }

    [Test, Order(6)]
    public void TestWindowInteractions()
    {
        recordedInteractions.Clear();
        MessageBox.Show(
            "测试 [窗口位移交互]: \n1. 请长程甩动窗口 (window_move)\n2. 请快速来回晃动窗口 (window_shake)\n\n完成后点击确定。",
            "人工指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        Assert.That(recordedInteractions, Does.Contain("桌宠被快速移动"), "未检测到快速位移");
        Assert.That(recordedInteractions, Does.Contain("桌宠被大幅晃动"), "未检测到幅度晃动");
    }

    [Test, Order(7)]
    public void TestChatInput()
    {
        recordedInputs.Clear();
        MessageBox.Show(
            "测试 [文本输入]: 请在桌宠底部的对话框输入 'Hello World' 并按回车。\n完成后点击确定。",
            "人工指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        Assert.That(recordedInputs, Does.Contain("Hello World"), "未收到正确的聊天输入 [Hello World]");
    }

    PetServer server = null!;
    readonly List<string> recordedInteractions = new();
    readonly List<string> recordedInputs = new();

    void AskUser(string question)
    {
        MessageBoxResult result = MessageBox.Show(question, "集成测试人工验证", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), $"验证失败: {question}");
    }

    [OneTimeSetUp]
    public async Task Setup()
    {
        server = new PetServer(Path.Combine(AlifePath.RuntimeFolderPath, "Alife.DeskPet.Client"), "Mao");
        server.OnInteracted += key => recordedInteractions.Add(key);
        server.OnInput += text => recordedInputs.Add(text);
        await server.WaitReadyAsync();
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await server.DisposeAsync();
    }
}
