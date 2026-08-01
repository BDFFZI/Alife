using Alife.Foundation;
using Alife.Function.Speech.VITS;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.Speech;

[TestFixture]
public class VitsSpeechTests
{
    VitsSpeechModel? _synth;

    [OneTimeSetUp]
    public void Init()
    {
        // AlifePath resolves RuntimeFolderPath automatically;
        // this call ensures the static paths are bootstrapped.
        Console.WriteLine($"[Test] RuntimeFolderPath = {AlifePath.RuntimeFolderPath}");
        Console.WriteLine($"[Test] VITS model dir     = {Path.Combine(AlifePath.RuntimeFolderPath, "VITS", "model")}");
        Console.WriteLine($"[Test] VITS src   dir     = {Path.Combine(AlifePath.RuntimeFolderPath, "VITS", "src")}");

        _synth = new VitsSpeechModel(new NullLogger<VitsSpeechModel>());
    }

    [OneTimeTearDown]
    public async Task Cleanup()
    {
        if (_synth != null)
            await _synth.DisposeAsync();
    }

    // ------------------------------------------------------------------ //
    // Test 1 – 基本合成：中文句子 → 生成 wav 文件
    // ------------------------------------------------------------------ //
    [Test]
    public async Task SynthesizeChinese_ReturnsValidWavFile()
    {
        Assert.That(_synth, Is.Not.Null, "VitsSpeechSynthesizer 初始化失败");

        string? wavPath = await _synth!.GenerateSpeechFileAsync("今天晚上吃什么好呢");

        Console.WriteLine($"[Test] 生成文件: {wavPath}");

        Assert.That(wavPath, Is.Not.Null.And.Not.Empty, "返回路径不应为空");
        Assert.That(File.Exists(wavPath), Is.True, $"文件不存在: {wavPath}");

        var info = new FileInfo(wavPath!);
        Assert.That(info.Length, Is.GreaterThan(1000), "wav 文件太小，可能是空音频");
    }

    // ------------------------------------------------------------------ //
    // Test 2 – 缓存：同一文本二次调用应直接返回缓存路径
    // ------------------------------------------------------------------ //
    [Test]
    public async Task SynthesizeSameText_ReturnsCachedPath()
    {
        Assert.That(_synth, Is.Not.Null);

        const string text = "缓存测试文本喵";
        string? path1 = await _synth!.GenerateSpeechFileAsync(text);
        string? path2 = await _synth!.GenerateSpeechFileAsync(text);

        Console.WriteLine($"[Test] 首次: {path1}");
        Console.WriteLine($"[Test] 二次: {path2}");

        Assert.That(path1, Is.EqualTo(path2), "相同文本应命中缓存，返回同一路径");
    }

    // ------------------------------------------------------------------ //
    // Test 3 – 空文本：应返回 null 而不是抛出异常
    // ------------------------------------------------------------------ //
    [Test]
    public async Task SynthesizeEmpty_ReturnsNull()
    {
        Assert.That(_synth, Is.Not.Null);
        string? result = await _synth!.GenerateSpeechFileAsync("");
        Assert.That(result, Is.Null, "空文本应返回 null");
    }

    // ------------------------------------------------------------------ //
    // Test 4 – 日文混合：中日混合文本
    // ------------------------------------------------------------------ //
    [Test]
    public async Task SynthesizeMixed_ChineseJapanese_ReturnsValidWavFile()
    {
        Assert.That(_synth, Is.Not.Null);

        string? wavPath = await _synth!.GenerateSpeechFileAsync("よろしく、我叫真央喵！");

        Console.WriteLine($"[Test] 混合文本生成文件: {wavPath}");

        Assert.That(wavPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(wavPath), Is.True);
    }
}
