using Alife.Function.DeskPet;
using System.IO;

namespace Alife.Test.DeskPet;

/// <summary>
/// 桌宠插件纯逻辑单元测试：验证 Live2D 模型元数据解析与路径解析。
/// （Electron 窗口模块无法在单元测试中以无头方式运行，故仅覆盖可测试的纯逻辑。）
/// </summary>
[TestFixture]
public class PetFunctionTests
{
    string rootDir = null!;
    string modelDir = null!;
    string model3JsonPath = null!;

    const string ModelName = "mao";

    [SetUp]
    public void SetUp()
    {
        rootDir = Path.Combine(Path.GetTempPath(), "AlifeTestDeskPet_" + Guid.NewGuid().ToString("N"));

        modelDir = Path.Combine(rootDir, ModelName);
        Directory.CreateDirectory(modelDir);
        model3JsonPath = Path.Combine(modelDir, $"{ModelName}.model3.json");

        string fallbackDir = Path.Combine(rootDir, "fallback");
        Directory.CreateDirectory(fallbackDir);
        File.WriteAllText(
            Path.Combine(fallbackDir, "fallback.model.json"),
            """
            {
              "FileReferences": {
                "Expressions": [],
                "Motions": {}
              }
            }
            """);

        File.WriteAllText(
            model3JsonPath,
            """
            {
              "FileReferences": {
                "Expressions": [
                  { "Name": "微笑", "File": "expressions/smile.exp3.json" },
                  { "Name": "繁星眼", "File": "expressions/star.exp3.json" }
                ],
                "Motions": {
                  "TapBody": [
                    { "Name": "点头", "File": "motions/nod.motion3.json" },
                    { "Name": "摇头", "File": "motions/shake.motion3.json" }
                  ]
                }
              },
              "Interaction": {
                "startup": [
                  { "text": "你好呀", "exp": "微笑", "mtn": { "group": "TapBody", "index": 0 } }
                ]
              }
            }
            """);
    }

    [TearDown]
    public void TearDown()
    {
        if (rootDir != null && Directory.Exists(rootDir))
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Test]
    public void LoadParsesExpressions()
    {
        PetModelMetadata metadata = PetModelMetadata.Load(modelDir);

        Assert.That(metadata.Expressions, Does.Contain("微笑"));
        Assert.That(metadata.Expressions, Does.Contain("繁星眼"));
        Assert.That(metadata.Expressions, Has.Exactly(2).Items);
    }

    [Test]
    public void LoadParsesMotions()
    {
        PetModelMetadata metadata = PetModelMetadata.Load(modelDir);

        Assert.That(metadata.Motions, Does.ContainKey("点头"));
        Assert.That(metadata.Motions["点头"], Is.EqualTo(("TapBody", 0)));
        Assert.That(metadata.Motions["摇头"], Is.EqualTo(("TapBody", 1)));
    }

    [Test]
    public void LoadParsesInteractions()
    {
        PetModelMetadata metadata = PetModelMetadata.Load(modelDir);

        Assert.That(metadata.Interactions, Does.ContainKey("startup"));
        List<InteractionItem> startup = metadata.Interactions["startup"];
        Assert.That(startup, Has.Exactly(1).Items);

        InteractionItem item = startup[0];
        Assert.That(item.Text, Is.EqualTo("你好呀"));
        Assert.That(item.Exp, Is.EqualTo("微笑"));
        Assert.That(item.Mtn, Is.Not.Null);
        Assert.That(item.Mtn!.Group, Is.EqualTo("TapBody"));
        Assert.That(item.Mtn.Index, Is.EqualTo(0));
    }

    [Test]
    public void LoadSetsModelPath()
    {
        PetModelMetadata metadata = PetModelMetadata.Load(modelDir);

        Assert.That(metadata.ModelPath, Is.EqualTo(model3JsonPath.Replace('\\', '/')));
    }
}