using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;
using Microsoft.Extensions.Logging;

public class MyModuleData
{
    [Description("随机的最大范围")]//默认的配置UI可以识别并向用户显示Description内容
    public int DefaultMax { get; set; } = 120;
}

[Module(
    "我的功能模块", "一个示例功能模块"
    // EditorUI = typeof(MyModuleUI) /*如果需要，可以用razor自定义模块界面，具体参考官方插件。否则默认使用预设的表单UI（注意：razor不支持热编译，你需要将其转为g.cs或dll）*/
)]//只要被打上Module标签的类就会被认为是功能模块，可以让用户勾选，或者也可以通过`角色文件夹/index.json`中的`Modules`属性来编辑启用的模块。
public class MyChatBehaviour(
    XmlFunctionCaller functionService,//可直接在构造函数申请其他模块，系统会自动通过依赖注入填充，此外XmlFunctionCaller提供函数调用的能力，是非常常用的基础模块
    ILogger<MyChatBehaviour> logger//也支持申请专用的logger，以及各种全局系统，具体可见 ChatActivitySystem 的创建过程
) :
    InteractiveModule<MyChatBehaviour>,/*封装好地模块基类，便于快速开发*/
    IConfigurable<MyModuleData>/*通过实现IConfigurable接入配置功能*/
{
    [XmlFunction(FunctionMode.OneShot)]// 表明该函数支持让AI通过Xml函数调用且格式为自闭合标签
    [Description("随机生成一个数字")]// 提供给AI的函数描述
    public Task Rand([Description("随机的最大范围")] int? max = null/*支持任何可被字符串转换的参数，包括默认值可选这些特性*/)
    {
        if (max == null)
            max = Configuration!.DefaultMax;//配置在模块构造后立即注入，故系统事件期间都是不为空的
        if (max < 0)
            throw new Exception("最大值必须大于 0");//可以正常抛出异常

        int value = Random.Shared.Next(max.Value);

        Poke("随机数结果：" + value);//向AI反馈结果(可选，如果函数的功能不需要返回结果，可以去除)
        //备注：Poke最终是通过ChatBot来与AI交互的，这是一个非常重要的类，如果要从根源上处理交互和上下文，就去获取ChatBot对象

        logger.LogInformation($"调用 {nameof(Rand)} 结果 {value}");//支持依赖注入的Logger

        return Task.CompletedTask;//如果有需要你可以使用异步代码
    }

    public MyModuleData? Configuration { get; set; }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        //将模块注册为xml处理器，以支持文档化和xml调用
        XmlHandler xmlHandler = new(this) {
            Description = "此服务可以为你提供一个生成随机数的功能。",
        };
        functionService.RegisterHandler(xmlHandler);
        //备注：xml函数调用还支持多次注册方式和额外功能，需要复杂的函数调用和注册机制，请查阅Alife.Function.FunctionCaller插件
    }
}
