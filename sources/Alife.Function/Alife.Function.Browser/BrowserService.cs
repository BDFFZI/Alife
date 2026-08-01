using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;

namespace Alife.Function.Browser;

public class BrowserConfig
{
    [Description("每页文本字符上限")]
    public int PageCharLimit { get; set; } = 1000;
}

[Module("浏览器工具",
    "让AI可以像人一样操控真实的浏览器，从而能够执行各种网页任务的同时，避免反爬。",
    defaultCategory: "Alife 官方/实用工具")]
public class BrowserService(
    XmlFunctionCaller functionService,
    IInteractor<BrowserService> interactor) :
    ChatBehaviour,
    IConfigurable<BrowserConfig>
{
    public BrowserConfig Configuration { get; set; } = null!;

    [XmlFunction(FunctionMode.OneShot)]
    public async Task OpenWebsite(string url)
    {
        await browser.OpenWebsiteAsync(url);
        interactor.Poke("已打开网站");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description($"返回当前打开网页的文本。其中可交互元素会以[{{交互类型}}{{id}}:{{描述}}]的格式返回。交互类型中：t=文本框，b=按钮；id则可在{nameof(GetElementInfo)}中用来查看对应元素的原始html信息")]
    public async Task ReadWebsite([Description("从1开始")] int page)
    {
        string result = await browser.ReadWebsiteAsync(page, Configuration.PageCharLimit);
        interactor.Poke($"页面内容：{result}\n提示：翻页寻找可疑的交互元素，然后使用{nameof(GetElementInfo)}查看");
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description($"查看{nameof(ReadWebsite)}中返回的可交互元素的原始完整html信息")]
    public async Task GetElementInfo(int id)
    {
        string result = await browser.GetElementInfoAsync(id);
        interactor.Poke("元素内容：" + result);
    }

    [XmlFunction(FunctionMode.Content)]
    public async Task RunWebsiteJs(XmlExecutorContext context, [XmlContent] string script)
    {
        if (context.CallMode == CallMode.Closing)
        {
            string code = context.FullContent.Trim();
            string result = await browser.RunWebsiteJsAsync(code);
            interactor.Poke($"JS结果：{result}\n注意：执行成功不代表符合预期，请验证执行结果");
        }
    }

    readonly BrowserEngine browser = new();

    protected override async Task OnAwake()
    {
        await browser.WaitToLoadedAsync(TimeSpan.FromSeconds(3));

        XmlHandler xmlHandler = new(this) {
            Description = "你的专属浏览器，当你需要上网查资料时使用",
            Explanation = $$"""
                            使用教程
                            1. 使用{{nameof(OpenWebsite)}}导航到目标网站
                            2. 等待导航完成后使用{{nameof(ReadWebsite)}}查看网页内容，并根据需要翻页查找。
                            3. 在页面内容中找到疑似要交互的元素后，调用{{nameof(GetElementInfo)}}，来查看元素的完整原始结构。
                            4. 确认元素内容后，思考对策，然后使用{{nameof(RunWebsiteJs)}}对其进行交互，比如点击输入等
                            5. 从2开始再次观察交互后的网页结果，并按需多次迭代，直到任务完成

                            使用提示
                            1. 此浏览器叫`Alife Browser`，注意区分
                            2. 遇到验证或登录，可请求用户人工操作来绕过反爬（浏览器默认最小化在托盘，需要告知用户打开）
                            3. 优先使用高质量搜索引擎，开源、个人网站，并跳过收费网站（如爱给网、百度文库等）
                            4. 找到可疑元素后务必调用{{nameof(GetElementInfo)}}来确认其真实结构，然后再根据情况进行下一步
                            5. 每次交互后要检查结果，比如输入文本后文本是否变化，借此确保交互成功或弄清楚网站的行为，以便进一步处理
                            6. 如果要下载浏览器中的内容可以先创建副本（如用canvas复制图片），然后用<a>触发下载，再从下载文件夹中拿文件，这样就能绕过验证机制
                            """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);
        functionService.AddPlainAreas(nameof(RunWebsiteJs));
    }
    protected override async Task OnDestroy()
    {
        await browser.DisposeAsync();
    }
}
