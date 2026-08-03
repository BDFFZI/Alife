using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Microsoft.Extensions.Logging;

namespace Alife.Demo.Plugin;

public class MyModuleConfig
{
    [DisplayName("默认最大值")]
    [Description("进行随机数生成时的默认最大范围")] //默认的配置UI可以识别 DisplayName,Description 属性来实现用户友好的显示
    public int DefaultMax { get; set; } = 120;
}

//Module 是插件中的功能注入的单元。系统会收集插件中的所有 Module，并将其显示在UI上供用户勾选。或者通过`角色文件夹/index.json`中的`Modules`属性也可以编辑 Character 启用的 Module。
//所有 Module 在 Character 激活时都会被放入到一个依赖注入容器中，对于被显式勾选的 Module，还会进行主动构造。
[Module("我的功能模块",
    "一个示例功能模块",
    defaultCategory: "我的插件", //Module在UI上可以分类显示，设定好默认类别可以方便用户查找
    EditorUI = typeof(MyModuleUI) //如果需要，可以用razor自定义模块界面，具体参考官方插件。否则默认使用预设的表单UI（注意：razor不支持热编译，你需要将其转为g.cs或dll）
)]
public class MyModule( //Module 可以通过依赖注入来获取其他系统、工具、插件对象，具体可见 ChatActivitySystem 的创建过程
    XmlFunctionCaller functionService, //XmlFunctionCaller 是一个常用的插件模块，借此可以轻松实现函数调用，是非常常用的基础模块
    ILogger<MyModule> logger, //可以申请专用的 logger，这不仅是一种规范，而且 logger 中记录的警告、报错将会实际的通过 UI 通知用户
    IInteractor<MyModule> interactor //当需要和 ai 交互时，使用专用的交换器，他可以自动格式化发给ai的文本，而且可以处理插件重载时的提示词注入问题
) :
    ChatBehaviour, //一个常用的特殊模块基类，使用该基类后，获取到 ChatActivity 上下文，以及其生命周期事件
    IConfigurable<MyModuleConfig> //通过实现 IConfigurable 接入配置功能
{
    public MyModuleConfig Configuration { get; set; } = null!; //配置功能会在构造函数之后注入非空的配置对象

    [XmlFunction(FunctionMode.OneShot)] // 表明该函数支持让AI通过Xml函数调用且格式为自闭合标签
    [Description("随机生成一个数字")] // 提供给AI的函数描述
    public Task Rand([Description("随机的最大范围")] int? max = null /*支持任何可被字符串转换的参数，包括默认值可选这些特性*/)
    {
        if (max == null)
            max = Configuration.DefaultMax; //配置在模块构造后立即注入，故系统事件期间都是不为空的

        if (max < 0)
            throw new Exception("最大值必须大于 0"); //可以正常抛出异常

        int value = Random.Shared.Next(max.Value);

        interactor.Poke("随机数结果：" + value); //向AI反馈结果(可选，如果函数的功能不需要返回结果，可以去除)
        //备注：Poke最终是通过ChatBot来与AI交互的，这是一个非常重要的类，如果要从根源上处理交互和上下文，就去获取ChatBot对象

        return Task.CompletedTask; //如果有需要你可以使用异步代码
    }

    protected override Task OnAwake()
    {
        if (Configuration.DefaultMax < 0)
            logger.LogWarning("默认最大值不能小于0，否则将无法产生随机数。");

        //将模块注册为xml处理器，以支持文档化和xml调用
        XmlHandler xmlHandler = new(this) {
            Description = "此服务可以为你提供一个生成随机数的功能。",
        };
        functionService.RegisterHandler(xmlHandler, cancellationToken: DestroyCancellationToken); //传入取消 token 可以在模块销毁时自动取消注册，方便进行热重载

        interactor.Prompt("..."); //可以利用 Prompt 功能额外添加一些自定义提示词，而且此功能添加的提示词是可以热更的

        return Task.CompletedTask;
    }
    protected override Task OnDestroy()
    {
        //只要模块正常执行了 OnAwake。那销毁时就会调用 OnDestroy，可借此进行销毁操作
        return Task.CompletedTask;
    }
    protected override async Task OnStart()
    {
        await interactor.ChatAsync("你好啊！"); //OnStart发生在同期模块都Awake之后，此时系统已完全创建，可以与ai进行正常交互了。
    }
    protected override Task OnUpdate()
    {
        if (UpdateContext.FrameCount % (int)(3 / UpdateContext.ExpectedDeltaTime) == 0)
        {
            //OnUpdate是一个安全的周期性回调，可以借此实现定时性功能
        }

        return Task.CompletedTask;
    }
}