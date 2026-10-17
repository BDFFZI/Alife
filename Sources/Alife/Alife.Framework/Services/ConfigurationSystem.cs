using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public class ConfigurationSystem(StorageSystem storageSystem)
{
    public Type? GetConfigurationType(Type target)
    {
        if (configurationTypes.TryGetValue(target, out Type? configurationType))
            return configurationType;

        Type[] interfaces = target.GetInterfaces();
        Type? targetInterface =
            interfaces.FirstOrDefault(value => value.IsGenericType && value.GetGenericTypeDefinition() == typeof(IConfigurable<>));
        if (targetInterface == null)
            return null;

        configurationType = targetInterface.GetGenericArguments()[0];
        configurationTypes[target] = configurationType;
        return configurationType;
    }
    public bool CanConfiguration(Type target)
    {
        return GetConfigurationType(target) != null;
    }
    public bool HasConfiguration(Type target, string root = "")
    {
        string path = Path.Combine(root, "Configuration", target.FullName!);
        return storageSystem.GetObject<JObject>(path) != null;
    }


    public object GetConfiguration(Type target, string root = "")
    {
        Type? configurationType = GetConfigurationType(target);
        if (configurationType == null)
            throw new Exception($"{target} 不支持配置功能。");

        JObject? configuration = storageSystem.GetObject<JObject>(Path.Combine(root, "Configuration", target.FullName!)) ??
                                 storageSystem.GetObject<JObject>(Path.Combine("Configuration", target.FullName!));
        if (configuration != null)
        {
            object? jsonResult = configuration.ToObject(configurationType, replaceSerializer);
            return jsonResult ?? throw new Exception($"{target} 无法被json反序列化。");
        }

        object? constructionResult = Activator.CreateInstance(configurationType, null);
        return constructionResult ?? throw new Exception($"{target} 无法被构造函数构造。");
    }
    public string GetConfigurationJson(Type target, string root = "")
    {
        return JsonConvert.SerializeObject(GetConfiguration(target, root), Formatting.Indented);
    }

    public void SetConfiguration(Type target, object configuration, string root = "")
    {
        Type? configurationType = GetConfigurationType(target);
        if (configurationType == null)
            throw new Exception("目标不支持配置功能！");
        if (configurationType.IsInstanceOfType(configuration) == false)
            throw new Exception("目标不支持当前配置类型！");

        storageSystem.SetObject(Path.Combine(root, "Configuration", target.FullName!), configuration);
    }
    public void SetConfigurationJson(Type target, string configurationJson, string root = "")
    {
        Type? configurationType = GetConfigurationType(target);
        if (configurationType == null)
            throw new Exception("目标不支持配置功能！");

        object? configObject = JsonConvert.DeserializeObject(configurationJson, configurationType, new JsonSerializerSettings {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        });
        if (configObject == null)
            throw new Exception("反序列化失败，无法创建对象。");

        SetConfiguration(target, configObject, root);
    }

    public void DeleteConfiguration(Type target, string root = "")
    {
        storageSystem.DeleteObject(Path.Combine(root, "Configuration", target.FullName!));
    }

    public string? GetConfigurationFilePath(Type target, string root = "")
    {
        if (GetConfigurationType(target) == null)
            return null;

        // 先检查角色配置
        if (!string.IsNullOrEmpty(root))
        {
            string rootPath = storageSystem.GetObjectAbsolutePath(Path.Combine(root, "Configuration", target.FullName!));
            if (File.Exists(rootPath))
                return rootPath;
        }

        // 回退到全局配置
        string globalPath = storageSystem.GetObjectAbsolutePath(Path.Combine("Configuration", target.FullName!));
        if (File.Exists(globalPath))
            return globalPath;

        return null;
    }

    readonly JsonSerializer replaceSerializer = new() { ObjectCreationHandling = ObjectCreationHandling.Replace };
    readonly Dictionary<Type, Type> configurationTypes = new();
}