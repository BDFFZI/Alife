using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Alife.Framework;

public class ConstructContainer
{
    public IReadOnlyList<object> Instances => instances;
    public event Func<object, Task>? InstanceCreated;

    public void ClearBuilders()
    {
        types.Clear();
    }

    public void RegisterBuilder(Type type, Func<Type, Task<object>>? builder = null, bool isSingleton = true)
    {
        types.Add((type, builder ?? DefaultBuilder, isSingleton));
    }

    public void UnRegisterBuilder(Type type)
    {
        types.RemoveAll(tuple => tuple.type == type);
    }

    public async Task AddInstance(object instance, bool owned = false)
    {
        instances.Add(instance);
        if (owned)
        {
            isOwned.Add(instance);

            if (InstanceCreated != null)
            {
                foreach (Func<object, Task> func in InstanceCreated.GetInvocationList().Cast<Func<object, Task>>())
                {
                    await func.Invoke(instance);
                }
            }
        }
    }

    public async Task RemoveInstance(object instance)
    {
        if (isOwned.Contains(instance))
            await TypeUtility.DisposeObject(instance);

        instances.Remove(instance);
        isOwned.Remove(instance);
    }

    public async Task<object> RequireInstance(Type type)
    {
        {
            object? instance = instances.FirstOrDefault(instance => instance.GetType().IsAssignableTo(type));
            if (instance != null)
                return instance;
        }

        (object instance, bool isSingleton)? builed = null;

        Type queryType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var tuple = types.FirstOrDefault(tuple => tuple.type == queryType);
        if (tuple.type != null)
        {
            builed = (await tuple.builder.Invoke(type), tuple.isSingleton);
        }
        else
        {
            foreach (var pair in types)
            {
                Type compatibleType = pair.type;
                if (compatibleType.IsGenericTypeDefinition && type.IsGenericType)
                    compatibleType = pair.type.MakeGenericType(type.GetGenericArguments());

                if (compatibleType.IsAssignableTo(type))
                {
                    builed = (await pair.builder(compatibleType), pair.isSingleton);
                    break;
                }
            }
        }

        if (builed == null)
            throw new Exception($"无法找到构造 {TypeUtility.GetReadableName(type)} 的方法，请确保在 {nameof(ConstructContainer)} 中注册了该类型或实例。");

        if (builed.Value.isSingleton)
            await AddInstance(builed.Value.instance, true);
        return builed.Value.instance;
    }

    public async ValueTask DisposeAsync(IProgress<(string, float)>? progress = null)
    {
        for (int index = instances.Count - 1; index >= 0; index--)
        {
            object instance = instances[index];
            if (isOwned.Contains(instance) == false)
                continue;

            progress?.Report(($"销毁 {TypeUtility.GetReadableName(instance.GetType())} 对象", 1 - (float)index / instances.Count));
            await TypeUtility.DisposeObject(instance);
        }
    }

    readonly List<(Type type, Func<Type, Task<object>> builder, bool isSingleton)> types = new();
    readonly List<object> instances = new();
    readonly HashSet<object> isOwned = new();

    async Task<object> DefaultBuilder(Type type)
    {
        ConstructorInfo? constructor = type.GetConstructors().SingleOrDefault();
        if (constructor == null)
            throw new Exception($"{TypeUtility.GetReadableName(type)} 构造失败，此类型没有可用的构造函数。");

        object?[] dependencies = new object[constructor.GetParameters().Length];
        for (int index = 0; index < constructor.GetParameters().Length; index++)
        {
            ParameterInfo parameterInfo = constructor.GetParameters()[index];

            try
            {
                dependencies[index] = await RequireInstance(parameterInfo.ParameterType);
            }
            catch (Exception ex)
            {
                if (parameterInfo.HasDefaultValue)
                    dependencies[index] = parameterInfo.DefaultValue;
                else
                    throw new Exception($"{TypeUtility.GetReadableName(type)} 构造失败，无法满足其依赖的参数条件。", ex);
            }
        }

        return constructor.Invoke(dependencies);
    }
}