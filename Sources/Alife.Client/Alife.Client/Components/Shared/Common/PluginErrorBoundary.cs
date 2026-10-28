using Alife.Foundation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Alife.Client.Components.Shared.Common;

/// <summary>
/// 插件界面的错误边界。
/// 插件组件在宿主渲染树里渲染，一旦在渲染期抛出异常（例如参数名不存在、泛型参数不合法、
/// 按旧签名编译导致 MissingMethodException），异常会一路冒泡到应用级 ErrorBoundary，
/// 而那一层的策略是弹出「系统崩溃」并退出进程。
/// 这里在插件挂载点外包一层边界：只记录日志并把异常限制在该区域内，其它功能不受影响。
/// </summary>
public class PluginErrorBoundary : ErrorBoundary
{
    /// <summary>插件界面出错时的回调（用于提示或埋点），可为空。</summary>
    [Parameter]
    public EventCallback<Exception> OnPluginError { get; set; }

    protected override Task OnErrorAsync(Exception exception)
    {
        AlifeLog.LogError(new Exception($"插件界面渲染失败：{exception.GetType().Name}: {exception.Message}", exception));

        if (OnPluginError.HasDelegate)
            _ = OnPluginError.InvokeAsync(exception);

        return base.OnErrorAsync(exception);
    }
}
