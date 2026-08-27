using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Alife.Framework;

public static class TypeUtility
{
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