using System;

namespace Alife.Function.DeskPet;

/// <summary>
/// 实现该接口的类将被Live2DDeskPet自动收集到依赖注入容器中创建，且可以通过接口成员注入网页内容
/// </summary>
public interface IPetModule
{
    string? CssCode => null;
    string? HtmlCode => null;
    string? JsCode => null;
}