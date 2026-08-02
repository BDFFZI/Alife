using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp; 

namespace Alife.PluginContext;

public class CSharpCompiler
{
    public void SetBasicDllFiles(IEnumerable<string> dllFiles)
    {
        baseDllReferences.Clear();
        foreach (var file in dllFiles)
            baseDllReferences.Add(MetadataReference.CreateFromFile(file));
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
}