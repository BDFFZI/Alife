using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alife.Framework;

public class ConfigurationSystem(StorageSystem storageSystem)
{
    public bool CanConfiguration(Type target)
    {
        return GetConfigurationType(target) != null;
    }
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
    public object? GetConfiguration(Type target, string root = "")
    {
        Type? configurationType = GetConfigurationType(target);
        if (configurationType == null)
            return null;

        JObject? configuration = storageSystem.GetObject<JObject>(Path.Combine(root, "Configuration", target.FullName!)) ??
                                 storageSystem.GetObject<JObject>(Path.Combine("Configuration", target.FullName!));
        if (configuration != null) return configuration.ToObject(configurationType, replaceSerializer);
        return Activator.CreateInstance(configurationType, null);
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
    public void DeleteConfiguration(Type target, string root = "")
    {
        storageSystem.DeleteObject(Path.Combine(root, "Configuration", target.FullName!));
    }
    public bool HasConfiguration(Type target, string root = "")
    {
        string path = Path.Combine(root, "Configuration", target.FullName!);
        return storageSystem.GetObject<JObject>(path) != null;
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
