using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Alife.Platform;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace Alife.Framework;

public class ModuleLoadContext(string[] managedDirectories, string[] unmanagedDirectories) : AssemblyLoadContext("ModuleContext", isCollectible: true)
{
    public Assembly LoadDll(string dllPath)
    {
        using var assemblyStream = new MemoryStream(File.ReadAllBytes(dllPath));
        string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        MemoryStream? pdbStream = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        Assembly assembly = LoadFromStream(assemblyStream, pdbStream);
        pdbStream?.Dispose();
        return assembly;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
        if (loaded != null)
            return loaded;

        string dllName = $"{assemblyName.Name}.dll";
        foreach (string dir in managedDirectories)
        {
            string path = Path.Combine(dir, dllName);
            if (File.Exists(path))
                return LoadDll(path);
        }
        return null;
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

    readonly string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? $"win-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? $"linux-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
            : $"osx-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
}

public class ModuleSystem
{
    public event Action? OnModulesReloaded;

    public string GetModuleFolderRoot()
    {
        return moduleRoot;
    }
    public StringFolder GetModuleFolder()
    {
        return moduleFolder;
    }

    public IEnumerable<Type> GetAllModules()
    {
        return moduleTypes.Values;
    }
    public Type? GetModule(string moduleID)
    {
        return moduleTypes.GetValueOrDefault(moduleID);
    }
    public string GetModuleID(Type moduleType)
    {
        return moduleType.FullName!;
    }
    public void ReloadModules()
    {
        //确保模块文件夹存在，防止报错
        if (Directory.Exists(moduleRoot) == false)
            Directory.CreateDirectory(moduleRoot);

        ReloadContext(CompileModule(moduleRoot));
        OnModulesReloaded?.Invoke();
    }
    public ModuleLoadContext CompileModule(string source)
    {
        //创建插件容器
        ModuleLoadContext compilingContext;
        {
            List<string> extraDirectories = new(2) {
                AppDomain.CurrentDomain.BaseDirectory
            };
            string baseDirectory = Path.Combine(moduleRoot, "BaseDirectory");
            if (Directory.Exists(baseDirectory))//插件文件夹BaseDirectory，与旧版本兼容
                extraDirectories.Add(baseDirectory);

            compilingContext = new(
                managedExtraDirectories.Concat(extraDirectories).ToArray(),
                unmanagedExtraDirectories.Concat(extraDirectories).ToArray()
            );
        }

        try
        {
            //加载插件目录的dll模块
            foreach (string file in Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    string assemblyName = AssemblyName.GetAssemblyName(file).FullName;
                    if (defaultAssemblies.Contains(assemblyName) == false)
                        compilingContext.LoadDll(file);
                }
                catch
                {
                    // ignored
                }
            }

            //热编译插件目录的cs模块
            {
                string dllPath = Path.Combine(AlifePath.TempFolderPath, "Modules.dll");
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

                //解析语法树
                var syntaxTrees = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
                    .Select(file => CSharpSyntaxTree.ParseText(
                        File.ReadAllText(file),
                        new CSharpParseOptions(LanguageVersion.Latest),
                        path: file,
                        encoding: System.Text.Encoding.UTF8))
                    .ToList();

                //收集dll环境
                var references = new List<MetadataReference>();
                var addedAssemblies = new HashSet<string>();

                List<string> collectionPath = new();
                collectionPath.AddRange(AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.Location));//已加载程序集
                collectionPath.AddRange(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"));//自包含程序集
                collectionPath.AddRange(Directory.GetFiles(source, "*.dll", SearchOption.AllDirectories));//插件自带程序集
                collectionPath.AddRange(managedExtraDirectories.Select(path => Directory.GetFiles(path, "*.dll")).SelectMany(strings => strings));//nuget程序集
                foreach (var file in collectionPath)
                {
                    try
                    {
                        var name = AssemblyName.GetAssemblyName(file);
                        if (addedAssemblies.Contains(name.Name!))
                            continue;
                        references.Add(MetadataReference.CreateFromFile(file));
                        addedAssemblies.Add(name.Name!);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                //编译
                var compilation = CSharpCompilation.Create(
                    "Modules",
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                        .WithAllowUnsafe(true)
                        .WithOptimizationLevel(OptimizationLevel.Release));

                var emitResult = compilation.Emit(dllPath, pdbPath);

                if (!emitResult.Success)
                {
                    var errors = string.Join("\n", emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                    throw new Exception($"模块编译失败:\n{errors}");
                }

                compilingContext.LoadDll(dllPath);
            }

            return compilingContext;
        }
        catch
        {
            compilingContext.Unload();
            throw;
        }
    }

    public void SetExtraContext(string[] managed, string[] unmanaged)
    {
        managedExtraDirectories = managed;
        unmanagedExtraDirectories = unmanaged;
    }

    public void SaveData()
    {
        storageSystem.SetObject(moduleSystemConfig, moduleFolder);
    }

#if DEBUG
    readonly string moduleRoot = Path.Combine(AlifePath.StorageFolderPath, "PluginsDebug");
#else
    readonly string moduleRoot = Path.Combine(AlifePath.StorageFolderPath, "Plugins");
#endif

    readonly string moduleSystemConfig = "Settings/ModuleCategory";
    readonly StorageSystem storageSystem;
    readonly Dictionary<string, Type> moduleTypes;
    readonly StringFolder moduleFolder;
    readonly ILogger<ModuleSystem> logger;

    readonly HashSet<string> defaultAssemblies;
    readonly Assembly[] alifeAssemblies;

    AssemblyLoadContext? moduleAssemblies;
    string[] managedExtraDirectories = [];
    string[] unmanagedExtraDirectories = [];

    public ModuleSystem(StorageSystem storageSystem, ILogger<ModuleSystem> logger)
    {
        this.storageSystem = storageSystem;
        this.logger = logger;

        moduleTypes = new Dictionary<string, Type>();
        moduleFolder = storageSystem.GetObject(moduleSystemConfig, new StringFolder("全部模块"))!;

        defaultAssemblies = AssemblyLoadContext.Default.Assemblies.Select(assembly => assembly.FullName).ToHashSet()!;
        alifeAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Alife") ?? false).ToArray();
        LoadAssemblyChain(Assembly.GetEntryAssembly()!);//加载所有本地自带的程序，方便后续判断

        void LoadAssemblyChain(Assembly entryAssembly)
        {
            var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<Assembly>();
            queue.Enqueue(entryAssembly);
            while (queue.Count > 0)
            {
                var assembly = queue.Dequeue();
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    // 如果这个程序集还没被加载过
                    if (!loadedAssemblies.Contains(reference.FullName))
                    {
                        try
                        {
                            // 强制加载它
                            var loaded = Assembly.Load(reference);
                            queue.Enqueue(loaded);
                            loadedAssemblies.Add(reference.FullName);
                        }
                        catch
                        {
                            // 忽略加载失败的程序集（有些可能是环境相关的）
                        }
                    }
                }
            }
        }
    }

    void ReloadContext(AssemblyLoadContext context)
    {
        //替换上下文
        moduleTypes.Clear();
        if (moduleAssemblies != null)
            moduleAssemblies.Unload();
        moduleAssemblies = context;

        //统计Module
        foreach (Assembly assembly in moduleAssemblies.Assemblies.Union(alifeAssemblies))
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception e)
            {
                logger.LogError(e, "程序集类型获取失败");
                continue;
            }

            foreach (Type type in types)
            {
                if (type.GetCustomAttribute<ModuleAttribute>() == null)
                    continue;
                if (type.IsAbstract)
                    continue;
                if (type.IsInterface)
                    continue;

                moduleTypes.Add(GetModuleID(type), type);
            }
        }

        SyncFolder();
    }
    void SyncFolder()
    {
        HashSet<string> currentModules = moduleTypes.Keys.ToHashSet();

        //移除无效模块，同时如果有效则剔除
        moduleFolder.RemoveAll(name => currentModules.Remove(name) == false);
        //剩下的就是还没有的模块，添加到根目录
        foreach (var typeName in currentModules)
        {
            ModuleAttribute? moduleAttribute = moduleTypes[typeName].GetCustomAttribute<ModuleAttribute>();
            if (moduleAttribute == null)
                continue;

            StringFolder folder = moduleFolder;
            string[] path = moduleAttribute.DefaultCategory.Split("/", StringSplitOptions.RemoveEmptyEntries);
            foreach (string subFolderName in path)
            {
                string name = subFolderName;
                StringFolder? subFolder = folder.Folders.FirstOrDefault(subFolder => subFolder.Name == name);
                if (subFolder == null)
                {
                    subFolder = new StringFolder(subFolderName);
                    folder.Folders.Add(subFolder);
                }

                folder = subFolder;
            }

            folder.Strings.Add(typeName);
        }
    }
}
