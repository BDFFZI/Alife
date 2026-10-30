// AntDesign Blazor 的主题是编译期 CSS（light / dark 两张样式表），运行时换主题就是整张换表；
// 同时把 data-theme 打在 <html> 上，让 alife-theme.css 里的令牌跟着翻转。
window.alifeTheme = (function () {
    const LIGHT = "_content/AntDesign/css/ant-design-blazor.css";
    const DARK = "_content/AntDesign/css/ant-design-blazor.dark.css";
    const STORAGE_KEY = "alife.theme";
    let current = null;

    function antdLink() {
        return [...document.querySelectorAll("link[rel=stylesheet]")]
            .find(link => (link.getAttribute("href") || "").includes("_content/AntDesign/css/"));
    }

    function set(mode) {
        const isDark = mode === "dark";
        if (current === mode) return;
        current = mode;

        document.documentElement.dataset.theme = mode;

        const link = antdLink();
        if (link) {
            link.href = isDark ? DARK : LIGHT;
        } else {
            const added = document.createElement("link");
            added.rel = "stylesheet";
            added.href = isDark ? DARK : LIGHT;
            document.head.appendChild(added);
        }
    }

    return {
        apply(mode) {
            set(mode);
            localStorage.setItem(STORAGE_KEY, mode);
        }
    };
})();

// 本脚本在 <head> 内同步执行：按上次记录先把主题切好，首帧就是最终配色，不会闪一下亮色
(function () {
    try {
        if (localStorage.getItem("alife.theme") === "dark")
            window.alifeTheme.apply("dark");
    } catch (e) {
        // localStorage 不可用时保持默认的亮色主题
    }
})();
