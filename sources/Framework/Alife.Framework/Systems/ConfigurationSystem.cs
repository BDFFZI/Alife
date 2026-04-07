using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public class ConfigurationSystem
{
    public Type? GetConfigurationType(Type target)
    {
        if (configurationTypes.TryGetValue(target, out Type? configurationType))
            return configurationType;

        Type[] interfaces = target.GetInterfaces();
        Type? targetInterface = interfaces.FirstOrDefault(value => value.IsGenericType && value.GetGenericTypeDefinition() == typeof(IConfigurable<>));
        if (targetInterface == null)
            return null;

        configurationType = targetInterface.GetGenericArguments()[0];
        configurationTypes[target] = configurationType;
        return configurationType;
    }
    public bool CanConfiguration(Type type)
    {
        return GetConfigurationType(type) != null;
    }
    public object? GetConfiguration(Type target)
    {
        Type? configurationType = GetConfigurationType(target);
        if (configurationType == null)
            return null; //不支持配置

        // 尝试从缓存获取
        if (configurations.TryGetValue(target, out JObject? configuration))
            return configuration.ToObject(configurationType);

        // 尝试从存储加载
        string key = $"Configuration/{target.FullName}";
        configuration = storageSystem.GetObject<JObject>(key);

        if (configuration != null)
        {
            configurations[target] = configuration;
            return configuration.ToObject(configurationType);
        }

        // 创建默认实例
        object? rawConfiguration = Activator.CreateInstance(configurationType, null);
        if (rawConfiguration == null)
            return null;

        return rawConfiguration;
    }
    public void SetConfiguration(Type target, object configuration)
    {
        if (CanConfiguration(target) == false)
            throw new Exception("目标类型不支持配置功能！");

        JObject json = JObject.FromObject(configuration);
        configurations[target] = json;

        // 针对每个插件单独保存
        string key = $"Configuration/{target.FullName}";
        storageSystem.SetObject(key, configuration);
    }
    public JObject? GetConfigurationJson(Type target)
    {
        object? configuration = GetConfiguration(target);
        if (configuration != null)
            return JObject.FromObject(configuration);
        return null;
    }

    public ConfigurationSystem(StorageSystem storageSystem)
    {
        this.storageSystem = storageSystem;
        configurations = new Dictionary<Type, JObject>();
        configurationTypes = new Dictionary<Type, Type>();
    }

    readonly StorageSystem storageSystem;
    readonly Dictionary<Type, JObject> configurations;
    readonly Dictionary<Type, Type> configurationTypes;
}
