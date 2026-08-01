using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Alife.Framework;
public static class TypeUtility
{
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