using System.Reflection;
using Alife.Framework;

namespace Alife.Client.Windows;

public class WindowsCSharpCompiler : CSharpCompiler
{
    protected override IEnumerable<string> GetAssemblyReferenceFiles(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .Select(assembly => assembly.Location)
            .Where(location => string.IsNullOrEmpty(location) == false && File.Exists(location));
    }
}