using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Alife.Function.TableGame;

/// <summary>
/// 游戏界面与游戏中心之间的唯一契约：中心只负责切换 <see cref="Opened"/>，游戏自己决定怎么退回到中心。
/// </summary>
public abstract class TableGameUI : ComponentBase
{
    [Parameter] public bool Opened { get; set; }
    [Parameter] public EventCallback CloseRequested { get; set; }

    protected Task RequestCloseAsync()
    {
        return CloseRequested.InvokeAsync();
    }
}
