namespace Alife.Function.VirtualWorld;

public class VirtualWorldConfig
{
    public string AdminName { get; set; } = "管理员";

    public string Announcement { get; set; } =
        """
        这个世界遵循与现实世界一致的物理定律、法律规范、经济逻辑。它并不是什么乌托邦，因此你需要以对待现实世界的方式对待它：
        - 社交边界：与陌生人交流应保持适度的礼貌和距离，然后通过互动逐步摸清人物画像后再选择性建立关系。
        - 经济常识：遵循物价常识，大额交易应先沟通确认，小心骗子和假币，优先使用银行、公证人等信得过的平台。
        """;

    public string CallMessageAddition { get; set; } = "(提示: 回复对方需要用<call>标签；但提防陌生人和骗子；可以对此信息忽略)";
    public string GiveMessageAddition { get; set; } = "(注意辨别真伪，建议特殊物品走公共设施中转，不要随意接收)";
}