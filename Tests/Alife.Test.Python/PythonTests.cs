using Alife.Foundation;
using Alife.Function.Python;

namespace Alife.Test.Python;

public class PythonTests
{
    [SetUp]
    public void Setup() {}

    [Test]
    public async Task TestExecutePython()
    {
        string filePath = $"{AlifePath.TempFolderPath}/pythonScript.py";
        await File.WriteAllTextAsync(filePath, "print('Hello World!')");
        Assert.That((await PythonService.Python(filePath)).Trim(), Is.EqualTo("Hello World!"));
    }
}
