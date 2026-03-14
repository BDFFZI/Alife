public class Character : ICloneable
{
    public string ID { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "猫娘女仆";
    public string Prompt { get; set; } = "你是一只可爱的猫娘女仆。";
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
