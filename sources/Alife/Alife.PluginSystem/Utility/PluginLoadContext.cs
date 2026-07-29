using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.PluginSystem;

public partial class PluginLoadContext
{
    public static PluginLoadContext? RootPluginContext { get; set; }
    static readonly Dictionary<string, Assembly> LoadedAssemblies = new();
    static readonly Dictionary<Assembly, PluginLoadContext> AssemblyOwners = new();
}

public partial class PluginLoadContext(string name, string[] unmanagedDirectories) : AssemblyLoadContext(isCollectible: true, name: name), IAsyncDisposable
{
    public bool IsDisposed => isDisposed;
    public event Func<Task>? Disposed;

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
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;
        isDisposed = true;

        foreach (PluginLoadContext childContext in childContexts)
            await childContext.DisposeAsync();
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

        if (Disposed != null)
        {
            try
            {
                await Task.WhenAll(Disposed.GetInvocationList()
                    .Cast<Func<Task>>()
                    .Select(func => func()));
            }
            catch (Exception e)
            {
                AlifeLog.LogError(e);
            }
        }
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

        IEnumerable<string> queryUnmanagedDirectories = unmanagedDirectories;
        if (RootPluginContext != null && this != RootPluginContext)
            queryUnmanagedDirectories = queryUnmanagedDirectories.Concat(RootPluginContext.unmanagedDirectories);

        foreach (string dir in queryUnmanagedDirectories)
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
    readonly string[] unmanagedDirectories = unmanagedDirectories;
    bool isDisposed;

    readonly string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? $"win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? $"linux-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
            : $"osx-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
}
