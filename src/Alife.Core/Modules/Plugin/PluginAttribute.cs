public class PluginAttribute : Attribute
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string? Url { get; private set; }
    public string[] Tags { get; private set; }
    public Type? ConfigurationUIType { get; set; }

    public PluginAttribute(string name, string description, string? url = null, string[]? pluginType = null, Type? configurationUIType = null)
    {
        Name = name;
        Description = description;
        Url = url;
        Tags = pluginType ?? [];
        ConfigurationUIType = configurationUIType;
    }
}
