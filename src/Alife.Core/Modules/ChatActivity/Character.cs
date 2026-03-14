public class Character : ICloneable
{
    public string ID { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "未命名角色";
    public string Prompt { get; set; } = "你是一个有用的助手。";
    public HashSet<Type> Plugins { get; set; } = new();
    public bool AutoActivate { get; set; }

    public object Clone()
    {
        return new Character() {
            ID = ID,
            Name = Name,
            Prompt = Prompt,
            Plugins = [..Plugins],
            AutoActivate = AutoActivate,
        };
    }
}
