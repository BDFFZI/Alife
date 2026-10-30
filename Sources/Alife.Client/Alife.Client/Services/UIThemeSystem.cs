using Alife.Foundation;

namespace Alife.Client;

/// <summary>
/// 明暗主题的唯一状态源：这里只管状态与持久化，真正换样式表由宿主布局调用脚本完成
/// （AntDesign 的主题是编译期 CSS，运行时只能整张换表）。
/// </summary>
public class UIThemeSystem
{
    public const string LightMode = "light";
    public const string DarkMode = "dark";

    /// <summary>主题变化事件，宿主布局订阅后把样式表切过去。</summary>
    public event Action? ThemeChanged;

    public string Mode { get; private set; } =
        AlifeConfig.GetBool(SettingsKey) ? DarkMode : LightMode;

    public bool IsDark => Mode == DarkMode;

    public void SetDark(bool isDark)
    {
        string mode = isDark ? DarkMode : LightMode;
        if (Mode == mode)
            return;

        Mode = mode;
        AlifeConfig.SetBool(SettingsKey, isDark);
        ThemeChanged?.Invoke();
    }

    const string SettingsKey = "UITheme.Dark";
}
