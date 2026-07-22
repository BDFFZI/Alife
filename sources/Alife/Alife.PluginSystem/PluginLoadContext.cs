using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Alife.PluginSystem;

public partial class PluginLoadContext
{
    static readonly Dictionary<string, Assembly> LoadedAssemblies = new();
    static readonly Dictionary<Assembly, PluginLoadContext> AssemblyOwners = new();
}

public partial class PluginLoadContext(string name, string[] unmanagedDirectories) : AssemblyLoadContext(isCollectible: true, name: name), IDisposable
{
    public bool IsDisposing => isDisposing;
    public void Complete()
    {
        foreach (Assembly assembly in Assemblies)
        {
            string? assemblyName = assembly.GetName().Name;
            if (assemblyName == null)
                continue;

            LoadedAssemblies.Add(assemblyName, assembly);
            AssemblyOwners.Add(assembly, this);
        }
    }
    public void Dispose()
    {
        if (isDisposing)
            return;
        isDisposing = true;

        foreach (PluginLoadContext childContext in childContexts)
            childContext.Dispose();
        foreach (PluginLoadContext parentContext in parentContexts)
            parentContext.childContexts.Remove(this);
        parentContexts.Clear();

        foreach (Assembly assembly in Assemblies)
        {
            string? assemblyName = assembly.GetName().Name;
            if (assemblyName == null)
                continue;

            LoadedAssemblies.Remove(assemblyName);
            AssemblyOwners.Remove(assembly);
        }
        Unload();
    }
    public Assembly LoadDll(string dllPath)
    {
        string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        var dllStream = new MemoryStream(File.ReadAllBytes(dllPath));
        MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        Assembly assembly = LoadFromStream(dllStream, pdbStream);
        dllStream.Dispose();
        pdbStream?.Dispose();
        return assembly;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null && LoadedAssemblies.TryGetValue(assemblyName.Name, out Assembly? load))
        {
            var parent = AssemblyOwners[load];
            parent.childContexts.Add(this);
            parentContexts.Add(parent);

            return load;
        }

        return base.Load(assemblyName);
    }
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        if (Path.HasExtension(unmanagedDllName) == false)
            unmanagedDllName += ".dll";

        foreach (string dir in unmanagedDirectories)
        {
            string[] candidatePaths = [
                Path.Combine(dir, "runtimes", rid, "native", unmanagedDllName),
                Path.Combine(dir, unmanagedDllName)
            ];

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                    return LoadUnmanagedDllFromPath(path);
            }
        }

        if (NativeLibrary.TryLoad(unmanagedDllName, out IntPtr handle))
            return handle;

        return IntPtr.Zero;
    }

    readonly HashSet<PluginLoadContext> childContexts = new();
    readonly HashSet<PluginLoadContext> parentContexts = new();
    bool isDisposing;

    readonly string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? $"win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? $"linux-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
            : $"osx-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
}
