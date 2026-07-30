using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Alife.PluginContext;

public class CSharpCompiler
{
    public void SetBasicDllFiles(IEnumerable<string> baseDllFiles)
    {
        baseDllReferences.Clear();
        baseAddedDlls.Clear();
        AddAssemblies(baseDllReferences, baseAddedDlls, baseDllFiles);
    }
    public void Compile(string outputDll, string[] csFiles, string[] dllFiles)
    {
        //解析cs语法树
        var syntaxTrees = csFiles.Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file),
                new CSharpParseOptions(LanguageVersion.Latest),
                path: file,
                encoding: System.Text.Encoding.UTF8))
            .ToList();

        //统计dll元数据
        List<MetadataReference> dllReferences = new(baseDllReferences);
        HashSet<string> addedDlls = new(baseAddedDlls);
        AddAssemblies(dllReferences, addedDlls, dllFiles);

        //编译
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputDll),
            syntaxTrees,
            dllReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release));

        string outputPdb = Path.ChangeExtension(outputDll, "pdb");
        var emitResult = compilation.Emit(outputDll, outputPdb);

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

    readonly List<MetadataReference> baseDllReferences = new();
    readonly HashSet<string> baseAddedDlls = new();

    static void AddAssemblies(List<MetadataReference> dllReferences, HashSet<string> addedDlls, IEnumerable<string> dllFiles)
    {
        //解析dll元数据
        foreach (var file in dllFiles)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(file);
                if (addedDlls.Contains(name.Name!))
                    continue;
                dllReferences.Add(MetadataReference.CreateFromFile(file));
                addedDlls.Add(name.Name!);
            }
            catch
            {
                // ignored
            }
        }
    }
}
