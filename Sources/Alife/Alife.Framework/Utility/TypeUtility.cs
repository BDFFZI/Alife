using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.Loader;
using Alife.Foundation;

namespace Alife.Framework;

public static class TypeUtility
{
    /// <summary>
    /// 枚举当前进程里所有已加载程序集中的类型，供特性扫描使用。
    /// 用 AssemblyLoadContext.All：内置代码在 Default 上下文，插件在各自独立上下文，只扫 AppDomain 会漏掉插件。
    /// </summary>
    public static IEnumerable<Type> GetAllLoadedTypes()
    {
        HashSet<Assembly> visited = new();

        foreach (AssemblyLoadContext context in AssemblyLoadContext.All)
        {
            foreach (Assembly assembly in context.Assemblies)
            {
                if (visited.Add(assembly) == false)
                    continue;

                foreach (Type type in GetAssemblyTypes(assembly))
                    yield return type;
            }
        }
    }

    /// <summary>
    /// 枚举某个加载上下文里已加载程序集中的类型，用于插件新增或重载后的增量扫描。
    /// </summary>
    public static IEnumerable<Type> GetLoadedTypes(AssemblyLoadContext assemblyLoadContext)
    {
        foreach (Assembly assembly in assemblyLoadContext.Assemblies)
        {
            foreach (Type type in GetAssemblyTypes(assembly))
                yield return type;
        }
    }

    /// <summary>
    /// 取单个程序集中的类型：程序集整体加载失败时返回空；单个类型读不到特性的那些由调用方自己处理。
    /// </summary>
    static IEnumerable<Type> GetAssemblyTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            //部分类型加载失败时，用能加载的那部分继续，不整体丢弃
            AlifeLog.LogError(new Exception($"扫描程序集 {assembly.GetName().Name} 时部分类型加载失败。", e));
            types = e.Types.OfType<Type>().ToArray();
        }
        catch (Exception e)
        {
            //插件与当前框架不兼容（方法签名已变化、依赖缺失）时，整个程序集跳过
            AlifeLog.LogError(new Exception($"扫描程序集 {assembly.GetName().Name} 失败，已跳过该程序集。", e));
            yield break;
        }

        foreach (Type type in types)
            yield return type;
    }

    public static bool IsInstanceUsingType(object instance, IList<Type> targetTypes)
    {
        //直接目标类型
        Type instanceType = instance.GetType();
        if (targetTypes.Contains(instanceType))
            return true;

        //通过泛型引用
        if (instanceType.IsGenericType && instanceType.GenericTypeArguments.Any(targetTypes.Contains))
            return true;

        //通过接口引用
        ConstructorInfo? constructor = instanceType.GetConstructors().SingleOrDefault();
        if (constructor != null)
        {
            foreach (var parameterInfo in constructor.GetParameters())
            {
                if (targetTypes.Any(parameterInfo.ParameterType.IsAssignableFrom))
                    return true;
            }
        }

        return false;
    }
    public static string GetReadableName(Type type)
    {
        DisplayNameAttribute? displayNameAttribute = type.GetCustomAttribute<DisplayNameAttribute>();
        if (displayNameAttribute != null)
            return displayNameAttribute.DisplayName;

        ModuleAttribute? moduleAttribute = type.GetCustomAttribute<ModuleAttribute>();
        if (moduleAttribute != null)
            return moduleAttribute.Name;

        return type.FullName ?? type.Name;
    }
    public static async Task DisposeObject(object instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;

            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
    public static async Task DisposeObjects(IEnumerable<object> objects)
    {
        foreach (object instance in objects.Reverse())
        {
            await DisposeObject(instance);
        }
    }
}
