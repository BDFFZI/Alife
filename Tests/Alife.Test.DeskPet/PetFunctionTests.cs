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
        TaskCompletionSource<bool> tcs = new();
        void OnPoke(string evt) => tcs.TrySetResult(true);
        client.OnPoke += OnPoke;
        
        MessageBoxResult result = MessageBox.Show(
            "请现在用鼠标左键双击真央（身体或者头部皆可）。\n双击后如果真央做出了语音反应与动作，请点击“是”。\n如果没有反应，请点击“否”。", 
            "人工交互测试：点击反应", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

        client.OnPoke -= OnPoke;

        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), "用户报告点击无反应。");
        Assert.That(tcs.Task.IsCompletedSuccessfully, Is.True, "后端未成功接收到前端透传的点击(Poke)消息！");
    }

    [Test, Order(6)]
    public void TestInteractionShake()
    {
        TaskCompletionSource<bool> tcs = new();
        void OnPoke(string evt)
        {
            if (evt.Contains("物理干扰")) tcs.TrySetResult(true);
        }
        client.OnPoke += OnPoke;
        
        MessageBoxResult result = MessageBox.Show(
            "请按住鼠标左键拖动真央，并在屏幕上快速大幅度甩动几圈。\n如果你看到真央出现了头晕星形眼，并反馈被晃晕了，请点击“是”。\n如果没有，请点击“否”。", 
            "人工交互测试：物理甩动", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);

        client.OnPoke -= OnPoke;

        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), "用户报告甩动无反应。");
        Assert.That(tcs.Task.IsCompletedSuccessfully, Is.True, "后端未成功接收到前端透传的摇晃(Poke)消息！");
    }

    DeskPetClient client = null!;

    void AskUser(string question)
    {
        MessageBoxResult result = MessageBox.Show(question, "人工单元测试验证", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.DefaultDesktopOnly);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes), $"人工验证失败: {question}");
    }

    [OneTimeSetUp]
    public void Setup()
    {
        client = new DeskPetClient();
        client.Start();
        // Give WPF and WebView2 time to render and load completely
        Thread.Sleep(3000);
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await client.DisposeAsync();
    }
}
