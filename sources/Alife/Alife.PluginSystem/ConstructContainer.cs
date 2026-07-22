using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Alife.PluginSystem;

public class ConstructContainer
{
    public IReadOnlyList<object> Instances => instances;

    public void RegisterBuilder(Type type, Func<Type, object>? builder = null)
    {
        builders.Add(type, builder ?? DefaultBuilder);
    }
    public void UnRegisterBuilder(Type type)
    {
        builders.Remove(type);
    }

    public void AddInstance(object instance)
    {
        instances.Add(instance);
    }
    public void RemoveInstance(object instance)
    {
        instances.Remove(instance);
    }

    public object RequireInstance(Type type)
    {
        object? instance = instances.FirstOrDefault(instance => instance.GetType().IsAssignableTo(type));
        if (instance != null)
            return instance;

        Func<Type, object>? builder = builders.GetValueOrDefault(type.IsGenericType ? type.GetGenericTypeDefinition() : type);
        if (builder != null)
            instance = builder.Invoke(type);
        else
        {
            foreach (var pair in builders)
            {
                Type compatibleType = pair.Key;
                if (compatibleType.IsGenericTypeDefinition && type.IsGenericType)
                    compatibleType = pair.Key.MakeGenericType(type.GetGenericArguments());

                if (compatibleType.IsAssignableTo(type))
                {
                    instance = pair.Value.Invoke(compatibleType);
                    break;
                }
            }
        }
        if (instance == null)
            throw new Exception("无可用的构造器:" + type);

        instances.Add(instance);
        return instance;
    }

    readonly Dictionary<Type, Func<Type, object>> builders = new();
    readonly List<object> instances = new();

    object DefaultBuilder(Type type)
    {
        ConstructorInfo? constructor = type.GetConstructors().SingleOrDefault();
        if (constructor == null)
            throw new NotSupportedException("类型不支持构造:" + type);

        object[] dependencies = constructor.GetParameters().Select(info => {
            try
            {
                return RequireInstance(info.ParameterType);
            }
            catch (Exception)
            {
                if (info.HasDefaultValue)
                    return info.DefaultValue!;
                throw;
            }
        }).ToArray();
        return constructor.Invoke(dependencies);
    }
}
