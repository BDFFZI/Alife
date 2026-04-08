using Alife.Function.DeskPet;
using System.Windows;

namespace Alife.Test.DeskPet;

[TestFixture]
public class PetFunctionTests
{
    [Test, Order(1)]
    public void TestBubble()
    {
        client.ShowBubble("你好，这是一个人工测试气泡！", 4000);
        AskUser("真央是否显示了内容为“你好，这是一个人工测试气泡！”的对话气泡？");
    }

    [Test, Order(2)]
    public void TestExpression()
    {
        client.SetExpression("繁星眼"); //开心(繁星眼)
        AskUser("真央的表情是否变成了星星眼（开心）？");
        
        client.SetExpression("微笑"); //恢复
    }

    [Test, Order(3)]
    public void TestMotion()
    {
        client.PlayMotion("TapBody", 0); // 害羞
        AskUser("真央是否播放了一个动作（例如身体扭动害羞）？");
    }

    [Test, Order(4)]
    public async Task TestPositionAndMove()
    {
        (double x, double y) = await client.GetPositionAsync();
        Assert.That(x, Is.GreaterThan(0));
        Assert.That(y, Is.GreaterThan(0));

        await client.MoveAsync(100, 100, 1000);
        
        AskUser("真央是否平滑地向右下方移动了？");
    }

    [Test, Order(5)]
    public void TestInteractionClick()
    {
        recordedPokes.Clear();
        MessageBox.Show(
            "测试 [单次点击]: 请双击真央（身体或头部）。\n完成操作后，再点击此窗口的“确定”。", 
            "指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        Assert.That(recordedPokes.Count, Is.GreaterThan(0), "在点击确定前，未检测到任何点击消息！");
    }

    [Test, Order(6)]
    public void TestComboClick()
    {
        recordedPokes.Clear();
        MessageBox.Show(
            "测试 [连续点击]: 请快速连续地点击真央 5 次以上，直到看到眩晕反应。\n完成操作后，再点击此窗口的“确定”。", 
            "指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        bool hasCombo = recordedPokes.Any(p => p.Contains("连击干扰"));
        Assert.That(hasCombo, Is.True, "在点击确定前，未检测到连击(Combo)消息！");
    }

    [Test, Order(7)]
    public void TestInteractionShake()
    {
        recordedPokes.Clear();
        MessageBox.Show(
            "测试 [摇晃]: 请按住鼠标左键甩动真央几圈，直到看到被晃晕的反应。\n完成操作后，再点击此窗口的“确定”。", 
            "指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        bool hasShake = recordedPokes.Any(p => p.Contains("物理干扰"));
        Assert.That(hasShake, Is.True, "在点击确定前，未检测到摇晃消息！");
    }

    [Test, Order(8)]
    public void TestChatInput()
    {
        recordedChats.Clear();
        MessageBox.Show(
            "测试 [文本输入]: 请在桌宠底部的输入框输入“Hello Mao”并按回车。\n完成操作后，再点击此窗口的“确定”。", 
            "指令", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

        bool hasChat = recordedChats.Any(c => c == "Hello Mao");
        Assert.That(hasChat, Is.True, "在点击确定前，未收到正确的聊天输入消息！");
    }

    DeskPetClient client = null!;
    List<string> recordedPokes = new();
    List<string> recordedChats = new();

    void AskUser(string question)
    {
        MessageBoxResult result = MessageBox.Show(question, "人工单元测试验证", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), $"人工验证失败: {question}");
    }

    [OneTimeSetUp]
    public void Setup()
    {
        client = new DeskPetClient();
        client.OnPoke += p => recordedPokes.Add(p);
        client.OnChat += c => recordedChats.Add(c);
        
        client.Start();
        // Give WPF time to load
        Thread.Sleep(3000);
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await client.DisposeAsync();
    }
}
