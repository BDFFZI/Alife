using System.Reflection;
using System.Runtime.Loader;
using Alife.Foundation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Alife.Framework;

public partial class CSharpCompiler
{
    public static IEnumerable<Assembly> GetAppCompilingAssemblies()
    {
        // Include transitive assemblies needed to compile plugin source.
        HashSet<string> loaded = new(StringComparer.OrdinalIgnoreCase);
        Queue<Assembly> queue = new(AssemblyLoadContext.Default.Assemblies);
        while (queue.TryDequeue(out var assembly))
        {
            if (assembly.FullName is null || loaded.Add(assembly.FullName) == false)
                continue;
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // 忽略加载失败的程序集（有些可能是环境相关的）
                }
            }
        }
        return AssemblyLoadContext.Default.Assemblies;
    }
}

public abstract partial class CSharpCompiler
{
    public void SetBasicDlls(IEnumerable<Assembly> dlls, IEnumerable<string>? dllFiles = null)
    {
        baseDllReferences.Clear();
        foreach (var file in GetAssemblyReferenceFiles(dlls).Concat(dllFiles ?? []))
            baseDllReferences.Add(MetadataReference.CreateFromFile(file));
    }
    public void Compile(string outputDll, string[] csFiles, string[] dllFiles)
    {
        //按当前平台注入预处理符号，使插件源码中的 #if ANDROID 等平台分支生效
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest, preprocessorSymbols: [AlifeContext.PlatformName.ToUpperInvariant()]);

        //解析cs语法树
        var syntaxTrees = csFiles.Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file),
                parseOptions,
                path: file,
                encoding: System.Text.Encoding.UTF8))
            .ToList();

        //统计dll元数据
        List<MetadataReference> dllReferences = new(baseDllReferences);
        foreach (var file in dllFiles)
            dllReferences.Add(MetadataReference.CreateFromFile(file));

        //编译
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputDll),
            syntaxTrees,
            dllReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release));

        string outputPdb = Path.ChangeExtension(outputDll, "pdb");
        EmitResult emitResult;
        using (Stream dllStream = File.Create(outputDll))
        using (Stream pdbStream = File.Create(outputPdb))
            emitResult = compilation.Emit(dllStream, pdbStream,
                options: OperatingSystem.IsWindows() ? null : new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        if (!emitResult.Success)
        {
            File.Delete(outputDll);
            File.Delete(outputPdb);

            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));

            throw new Exception($"模块编译失败:\n{errors}");
        }
    }

    /// <summary>
    /// windows可以通过Assembly.Location获取，但android由于程序集都被打包，没有location，必须手动解包，因此无法自动获取
    /// </summary>
    /// <param name="assemblies"></param>
    /// <returns></returns>
    protected abstract IEnumerable<string> GetAssemblyReferenceFiles(IEnumerable<Assembly> assemblies);

    readonly List<MetadataReference> baseDllReferences = new();

    protected CSharpCompiler()
    {
        SetBasicDlls(GetAppCompilingAssemblies());
    }
}