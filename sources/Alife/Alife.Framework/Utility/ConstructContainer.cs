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

    /// <summary>移除实例，并递归销毁依赖它的整条依赖链（先销毁依赖方，最后销毁被依赖方）。</summary>
    public async Task RemoveInstance(object instance)
    {
        //递归收集所有直接或间接依赖 instance 的对象
        List<object> chain = new();
        void Collect(object target)
        {
            if (dependents.TryGetValue(target, out List<object>? list) == false)
                return;
            foreach (object dependent in list)
            {
                if (chain.Contains(dependent) || dependent == instance)
                    continue;
                chain.Add(dependent);
                Collect(dependent);
            }
        }
        Collect(instance);

        //从最远的依赖方开始销毁（先销毁依赖它们的更下游，保证销毁时其依赖仍可用）
        for (int index = chain.Count - 1; index >= 0; index--)
        {
            object dependent = chain[index];
            await RemoveObject(dependent);
        }

        await RemoveObject(instance);

        async Task RemoveObject(object target)
        {
            if (isOwned.Contains(target))
                await TypeUtility.DisposeObject(target);

            instances.Remove(target);
            isOwned.Remove(target);
            dependents.Remove(target);
            dependencies.Remove(target);
        }
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
    
    /// <summary>反查某实例直接依赖了哪些实例（O(1)），用于从启用模块出发递归统计需要保留的实例。</summary>
    public IReadOnlyList<object> GetInstanceDependents(object instance)
    {
        return dependencies.TryGetValue(instance, out List<object>? list) ? list : [];
    }

    /// <summary>反查某实例被哪些实例直接依赖（O(1)），可用于判断对象是否被启用模块依赖。</summary>
    public IReadOnlyList<object> GetDependentsOf(object instance)
    {
        return dependents.TryGetValue(instance, out List<object>? list) ? list : [];
    }
    
    /// <summary>从根实例出发，递归收集所有直接或间接被依赖的实例（含根自身）。</summary>
    public List<object> CollectDependents(IEnumerable<object> roots)
    {
        HashSet<object> collected = new();
        Queue<object> pending = new();

        void Keep(object instance)
        {
            if (collected.Add(instance))
                pending.Enqueue(instance);
        }

        foreach (object root in roots)
            Keep(root);

        while (pending.TryDequeue(out object? current))
        {
            foreach (object dependency in GetInstanceDependents(current))
                Keep(dependency);
        }

        return collected.ToList();
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
    readonly Dictionary<object, List<object>> dependents = new(); //被依赖项索引：实例 → 依赖它的实例列表（O(1)反向查找）
    readonly Dictionary<object, List<object>> dependencies = new(); //依赖项索引：实例 → 它直接依赖的实例列表（O(1)正向查找）

    async Task<object> DefaultBuilder(Type type)
    {
        ConstructorInfo? constructor = type.GetConstructors().SingleOrDefault();
        if (constructor == null)
            throw new Exception($"{TypeUtility.GetReadableName(type)} 构造失败，此类型没有可用的构造函数。");

        object?[] ctorDependencies = new object[constructor.GetParameters().Length];
        for (int index = 0; index < constructor.GetParameters().Length; index++)
        {
            ParameterInfo parameterInfo = constructor.GetParameters()[index];

            try
            {
                ctorDependencies[index] = await RequireInstance(parameterInfo.ParameterType);
            }
            catch (Exception ex)
            {
                if (parameterInfo.HasDefaultValue)
                    ctorDependencies[index] = parameterInfo.DefaultValue;
                else
                    throw new Exception($"{TypeUtility.GetReadableName(type)} 构造失败，无法满足其依赖的参数条件。", ex);
            }
        }

        object instance = constructor.Invoke(ctorDependencies);

        //建立双向依赖索引：正向（实例依赖了谁）+ 反向（谁依赖了该实例）
        foreach (object? dependency in ctorDependencies)
        {
            if (dependency != null && instances.Contains(dependency))
            {
                if (dependents.TryGetValue(dependency, out List<object>? dependentsList) == false)
                    dependents[dependency] = dependentsList = new List<object>();
                if (dependentsList.Contains(instance) == false)
                    dependentsList.Add(instance);

                if (dependencies.TryGetValue(instance, out List<object>? dependenciesList) == false)
                    dependencies[instance] = dependenciesList = new List<object>();
                if (dependenciesList.Contains(dependency) == false)
                    dependenciesList.Add(dependency);
            }
        }

        return instance;
    }
}