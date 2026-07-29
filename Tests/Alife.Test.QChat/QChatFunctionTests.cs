using Alife.Function.QChat;
using NUnit.Framework;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using Alife.Platform;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatFunctionTests
{
    [OneTimeSetUp]
    public async Task Setup()
    {
        client = new OneBotClient(TestUrl);
        await client.ConnectAsync();
    }

    #region 私聊测试 (Private Chat)

    [Test, Order(1)]
    public async Task TestPrivate_TextSendRecv()
    {
        OneBotMessageEvent received = await EnsurePrivateAnchor();

        await client.SendPrivateMessage(lastPrivateUserId, $"[Echo] 你好，收到你的私聊: {received.RawMessage}");

        MessageBoxResult result = MessageBox.Show($"已原路回复。收到你的消息了吗？\n内容: {received.RawMessage}", "人工验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(2)]
    public async Task TestPrivate_ImageSend()
    {
        await EnsurePrivateAnchor();

        // 使用一个网图进行测试
        const string TestImageUrl = "https://www.google.com/images/branding/googlelogo/2x/googlelogo_color_272x92dp.png";
        await client.SendPrivateMessage(lastPrivateUserId,$"[CQ:image,file={TestImageUrl}]");

        MessageBoxResult result = MessageBox.Show("Bot 是否发送了一张图片（Google Logo）给你？", "图片发送验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(3)]
    public async Task TestPrivate_FileUpload()
    {
        await EnsurePrivateAnchor();

        string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "private_test.txt");
        await File.WriteAllTextAsync(tempFile, $"Private File Test - {DateTime.Now}");

        await client.UploadPrivateFile(lastPrivateUserId, tempFile, "私聊测试文件.txt");

        MessageBoxResult result = MessageBox.Show("QQ 是否收到文件 '私聊测试文件.txt'？", "文件发送验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    #endregion

    #region 群聊测试 (Group Chat)

    [Test, Order(4)]
    public async Task TestGroup_TextSendRecv()
    {
        OneBotMessageEvent received = await EnsureGroupAnchor();

        await client.SendGroupMessage(lastGroupId, $"[GroupEcho] 收到来自 {received.UserId} 的群消息: {received.RawMessage}");

        MessageBoxResult result = MessageBox.Show("Bot 是否在群里原路回复了消息？", "群聊验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(5)]
    public async Task TestGroup_AtCheck()
    {
        await EnsureGroupAnchor();

        TaskCompletionSource<OneBotMessageEvent> tcs = new();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.GroupId == lastGroupId && m.RawMessage.Contains($"[CQ:at,qq={client.BotId}"))
                tcs.TrySetResult(m);
        };
        client.EventReceived += handler;

        MessageBox.Show($"请在群里【@机器人】一下 ({client.BotId})", "群聊 At 测试");
        OneBotMessageEvent received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.EventReceived -= handler;

        // 测试机器人主动 @ 别人 (刚才说话的那个人)
        await client.SendGroupMessage(lastGroupId, $"[CQ:at,qq={received.UserId}] 收到你的召唤！这是机器人主动 @ 你的测试。");

        MessageBoxResult result = MessageBox.Show("Bot 是否成功检测到 @ 并主动回复且 @ 了你？", "At 验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(6)]
    public async Task TestGroup_ImageRecv()
    {
        TaskCompletionSource<OneBotMessageEvent> tcs = new();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.RawMessage.Contains("[CQ:image"))
                tcs.TrySetResult(m);
        };
        client.EventReceived += handler;

        MessageBox.Show("请在群里发送【一张图片】...", "群聊接收图片测试");
        OneBotMessageEvent received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.EventReceived -= handler;

        // 简易解析 CQ 码里的 file 参数 (实际建议用正则)
        string raw = received.RawMessage;
        Match match = Regex.Match(raw, "url=(.*?),");
        string fileUrl = WebUtility.HtmlDecode(match.Groups[1].Value);

        using HttpClient http = new();
        byte[] data = await http.GetByteArrayAsync(fileUrl);
        Assert.That(data.Length, Is.Not.EqualTo(0));
    }

    [Test, Order(7)]
    public async Task TestGroup_ImageSend()
    {
        await EnsureGroupAnchor();

        await client.SendGroupMessage(lastGroupId, $"[CQ:image,file=https://www.baidu.com/img/flexible/logo/pc/result.png]");

        MessageBoxResult result = MessageBox.Show("Bot 是否在群里发送了一张图片（百度 Logo）？", "群图片验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    [Test, Order(8)]
    public async Task TestGroup_FileRecv()
    {
        await EnsureGroupAnchor();

        TaskCompletionSource<OneBotNoticeEvent> tcs = new();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotNoticeEvent n && n.GroupId == lastGroupId && n.NoticeType == "group_upload")
                tcs.TrySetResult(n);
        };
        client.EventReceived += handler;

        MessageBox.Show("请在群里【上传一个文件】...", "群聊接收文件测试");
        OneBotNoticeEvent received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.EventReceived -= handler;

        Console.WriteLine($"收到群文件通知: {received.File?.Name}");

        // 关键：换取 URL (群文件需使用专门的 get_group_file_url 接口)
        if (received.File != null)
        {
            OneBotFile? info = await client.GetGroupFileUrl(received.GroupId, received.File.Id);
            if (info != null && string.IsNullOrEmpty(info.Url) == false)
            {
                Console.WriteLine($"[验证成功] 拿到文件 URL: {info.Url}");
                string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"recv_{received.File.Name}");
                await AlifeUtility.DownloadFileAsync(info.Url, savePath);
                Console.WriteLine($"[验证成功] 已将文件下载至: {savePath}");
            }
            else
            {
                Assert.Fail("无法换取文件 URL。");
            }
        }
    }

    [Test, Order(9)]
    public async Task TestGroup_FileUpload()
    {
        await EnsureGroupAnchor();

        string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "group_test.txt");
        await File.WriteAllTextAsync(tempFile, $"Group File Test - {DateTime.Now}");

        await client.UploadGroupFile(lastGroupId, tempFile, "群测试文件.txt");

        MessageBoxResult result = MessageBox.Show("群里是否已经收到文件 '群测试文件.txt'？", "群文件验证", MessageBoxButton.YesNo);
        Assert.That(result, Is.EqualTo(MessageBoxResult.Yes));
    }

    #endregion

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await client.DisposeAsync();
    }

    async Task<OneBotMessageEvent> EnsurePrivateAnchor()
    {
        if (lastPrivateMessage != null) return lastPrivateMessage;

        TaskCompletionSource<OneBotMessageEvent> tcs = new();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.MessageType == OneBotMessageType.Private) tcs.TrySetResult(m);
        };
        client.EventReceived += handler;

        MessageBox.Show("请给 Bot 发送一条【私聊】消息以锚定身份...", "私聊测试 - 自动锚定");
        lastPrivateMessage = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.EventReceived -= handler;

        lastPrivateUserId = lastPrivateMessage.UserId;
        return lastPrivateMessage;
    }

    async Task<OneBotMessageEvent> EnsureGroupAnchor()
    {
        if (lastGroupMessage != null) return lastGroupMessage;

        TaskCompletionSource<OneBotMessageEvent> tcs = new();
        Action<OneBotBaseEvent> handler = e => {
            if (e is OneBotMessageEvent m && m.MessageType == OneBotMessageType.Group) tcs.TrySetResult(m);
        };
        client.EventReceived += handler;

        MessageBox.Show("请在群里发送一条【普通群消息】以锚定群聊...", "群聊测试 - 自动锚定");
        lastGroupMessage = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.EventReceived -= handler;

        lastGroupId = lastGroupMessage.GroupId;
        lastGroupUserId = lastGroupMessage.UserId;
        return lastGroupMessage;
    }

    OneBotClient client = null!;
    OneBotMessageEvent? lastPrivateMessage;
    OneBotMessageEvent? lastGroupMessage;
    long lastPrivateUserId;
    long lastGroupId;
    long lastGroupUserId;
    const string TestUrl = "ws://127.0.0.1:3001";
}
