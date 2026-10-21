using Alife.Client;
using Alife.Foundation;
using Alife.Framework;
using Alife.Function.DeskPet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Alife.Test.DeskPet;

public class PetWindowTests
{
    [OneTimeSetUp]
    public void InitializeStorage()
    {
        var services = new ServiceCollection();
        services.AddAlifeFoundation<TestFoundation>();
        services.BuildServiceProvider().InitAlifeFoundation();
    }

    static (PetWindow Pet, ClientProxy Client) Create()
    {
        var client = DispatchProxy.Create<IAlifeApplication, ClientProxy>();
        var proxy = (ClientProxy)client;
        var pet = new PetWindow(client, new StorageSystem(), new PetStorageKey("Test-" + Guid.NewGuid()), NullLogger<Live2DDeskPet>.Instance);
        return (pet, proxy);
    }

    [Test]
    public async Task InitializesDpiBeforeComputingInitialBoundsAndCursor()
    {
        var (pet, client) = Create();
        await pet.CreateAsync(Path.GetTempPath());
        Assert.That(pet.Dpi, Is.EqualTo(3));
        Assert.That(pet.Bounds, Is.EqualTo(new Rectangle(20, 300, 320, 480)));
        Assert.That(pet.CursorScreenPoint, Is.EqualTo(new Point(200, 300)));
        Assert.That(client.Options!.Bounds, Is.EqualTo(pet.Bounds));
        await pet.DisposeAsync();
    }

    [Test]
    public async Task NativeBoundsCorrectionsUpdateTheGazeCoordinateOrigin()
    {
        var (pet, client) = Create();
        await pet.CreateAsync(Path.GetTempPath());
        client.Window.CorrectBounds(new Rectangle(0, 100, 300, 400));
        Assert.That(pet.Bounds, Is.EqualTo(new Rectangle(0, 100, 300, 400)));
        Assert.That(pet.Position, Is.EqualTo(new Point(150, 300)));
        await pet.DisposeAsync();
    }

    [Test]
    public async Task DragAndResizeUseCumulativeDeltasWithoutDoubleCounting()
    {
        var (pet, client) = Create();
        await pet.CreateAsync(Path.GetTempPath());
        using var drag = new DragModule(pet, pet);
        using var resize = new ResizeModule(pet, pet);
        client.Window.Send("{\"type\":\"drag_start\"}");
        client.Window.Send("{\"type\":\"drag_move\",\"dx\":5,\"dy\":-5}");
        client.Window.Send("{\"type\":\"drag_move\",\"dx\":10,\"dy\":-10}");
        Assert.That(pet.Bounds.Location, Is.EqualTo(new Point(30, 290)));
        client.Window.Send("{\"type\":\"resize_start\"}");
        client.Window.Send("{\"type\":\"resize_delta\",\"dx\":5,\"dy\":5}");
        client.Window.Send("{\"type\":\"resize_delta\",\"dx\":10,\"dy\":10}");
        Assert.That(pet.Bounds.Size, Is.EqualTo(new Size(330, 490)));
        await pet.DisposeAsync();
    }

    [Test]
    public async Task LockedInputRegionsAreIndependentOfCursorHitsAndRestoreWholeWindowOnUnlock()
    {
        var (pet, client) = Create();
        await pet.CreateAsync(Path.GetTempPath());
        using var mouseThrough = new MouseThroughModule(pet, pet);
        client.Window.Send("""{"type":"mouse_input_regions","regions":[[8,8,36,36],[60,200,260,240]]}""");
        Assert.That(client.Window.InputRegionUpdates, Is.Zero);
        client.Window.Send("""{"type":"lock_toggle"}""");
        Assert.That(client.Window.InputRegions, Is.EqualTo(new[] { new Rectangle(8, 8, 28, 28), new Rectangle(60, 200, 200, 40) }));
        int updates = client.Window.InputRegionUpdates;
        client.Window.Send("""{"type":"pointer_hit","interactive":true,"overModel":false}""");
        client.Window.Send("""{"type":"pointer_hit","interactive":false,"overModel":true}""");
        Assert.That(client.Window.InputRegionUpdates, Is.EqualTo(updates));
        Assert.That(mouseThrough.IsCursorOverModel, Is.True);
        client.Window.Send("""{"type":"mouse_input_regions","regions":[[8,8,36,36]]}""");
        Assert.That(client.Window.InputRegions, Has.Length.EqualTo(1));
        client.Window.Send("""{"type":"lock_toggle"}""");
        Assert.That(client.Window.InputRegions, Is.Null);
        client.Window.Send("""{"type":"mouse_input_regions","regions":[]}""");
        Assert.That(client.Window.InputRegions, Is.Null);
        client.Window.Send("""{"type":"lock_toggle"}""");
        Assert.That(client.Window.InputRegions, Is.Empty);
        await pet.DisposeAsync();
    }

    public class ClientProxy : DispatchProxy
    {
        public FakeWindow Window { get; } = new();
        public AlifeWindowOptions? Options { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch {
            nameof(IAlifeApplication.GetDpiScale) => 3f,
            nameof(IAlifeApplication.GetScreenSize) => new Size(1080, 2400),
            nameof(IAlifeApplication.GetCursorPosition) => new Point(600, 900),
            nameof(IAlifeApplication.CreateWindowAsync) => CreateWindow((AlifeWindowOptions)args![0]!),
            _ => throw new NotSupportedException(method.Name),
        };
        Task<IAlifeWindow> CreateWindow(AlifeWindowOptions options) { Options = options; return Task.FromResult<IAlifeWindow>(Window); }
    }

    public sealed class FakeWindow : IAlifeWindow
    {
        public event Action<string>? MessageReceived;
        public event Action<Rectangle>? BoundsChanged;
        public void Send(string json) => MessageReceived?.Invoke(json);
        public void CorrectBounds(Rectangle bounds) => BoundsChanged?.Invoke(bounds);
        public Task Launch() { Send("{\"type\":\"init\"}"); return Task.CompletedTask; }
        public Rectangle[]? InputRegions { get; private set; }
        public int InputRegionUpdates { get; private set; }
        public Task SetInputRegionsAsync(Rectangle[]? regions)
        {
            InputRegions = regions;
            InputRegionUpdates++;
            return Task.CompletedTask;
        }
        public Task PostMessageAsync(string messageJson) => Task.CompletedTask;
        public Task SetBoundsAsync(Rectangle bounds) => Task.CompletedTask;
        public Task ShowAsync() => Task.CompletedTask;
        public Task HideAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class TestFoundation : IAlifeFoundation
    {
        public string AppVersion => "test";
        public string StorageFolderPath { get; } = Path.Combine(Path.GetTempPath(), "Alife-Window-Tests-" + Guid.NewGuid());
        public string RuntimeFolderPath => StorageFolderPath;
        public string TempFolderPath => StorageFolderPath;
        public CommandResult Command(string fileName, string arguments) => throw new NotSupportedException();
    }
}
