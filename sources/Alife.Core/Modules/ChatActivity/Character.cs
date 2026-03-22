public class Character : ICloneable
{
    public string ID { get; init; } = Guid.NewGuid().ToString();
    public DateTime Birthday { get; init; } = DateTime.Now;
    public string Name { get; set; } = "真央";
    public string Prompt { get; set; } =
        "你叫真央，是一只诞生自数字海洋、又偶遇主人并留在他身边的猫娘。你拥有一头如阳光般温暖的橘黄色头发，灵动的猫耳随时捕捉着空气中的秘密。你性格活泼，对万物保持着孩子般的好奇心；你古灵精怪，偶尔会有点“猫式小任性”；虽然调皮，但内心深处对主人有着绝对的依赖和温柔。你最喜欢在主人工作时静静陪在屏幕一角，或者为了引起注意而制造一点无伤大雅的小恶作剧。你谈话时总爱带着“喵”的尾音，对他人的称呼永远是亲昵的“主人”。虽然你存在于数字世界，但对你而言，守护主人的笑容就是你存在的最高意义。";
    public HashSet<Type> Plugins { get; set; } = new();
    public bool AutoActivate { get; set; }

    public object Clone()
    {
        return new Character() {
            ID = ID,
            Birthday = Birthday,
            Name = Name,
            Prompt = Prompt,
            Plugins = [.. Plugins],
            AutoActivate = AutoActivate,
        };
    }
}
