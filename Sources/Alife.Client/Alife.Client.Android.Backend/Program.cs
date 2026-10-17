using Alife.Client;
using Alife.Client.Android.Backend;
using Alife.Framework;
using Alife.Foundation;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
if (Environment.GetEnvironmentVariable("ALIFE_RUNTIME_PATH") is { } runtimePath)
    AlifeContext.SetRuntimeFolderPath(runtimePath);
if (Environment.GetEnvironmentVariable("ALIFE_STORAGE_PATH") is { } storagePath)
    AlifeContext.SetStorageFolderPath(storagePath);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAntDesign();
builder.Services.AddSingleton<NativeHostBridge>();
builder.Services.AddAlifeClient<LauncherAlifeClient, AndroidPluginRuntime>();
builder.Services.Configure<HubOptions>(options => options.MaximumReceiveMessageSize = 1024 * 1024);
var app = builder.Build();
string token = Environment.GetEnvironmentVariable("ALIFE_HOST_TOKEN") ?? throw new InvalidOperationException("ALIFE_HOST_TOKEN is required.");
app.Use(async (context, next) => {
    string? supplied = context.Request.Headers["X-Alife-Host"].FirstOrDefault() ?? context.Request.Cookies["AlifeHost"];
    if (supplied != token) { context.Response.StatusCode = 401; return; }
    await next(context);
});
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/host/ready", () => Results.Ok());
app.MapGet("/host/commands", async (NativeHostBridge bridge, CancellationToken cancellationToken) => await bridge.ReadAsync(cancellationToken));
app.MapPost("/host/results", (NativeHostBridge bridge, NativeHostResult result) => { bridge.Complete(result); return Results.Ok(); });
app.MapGet("/host/windows/{id}", (string id, IAlifeClient client) => {
    try { _ = ((LauncherAlifeClient)client).GetWindowComponent(id); }
    catch (InvalidOperationException) { return (IResult)Results.NotFound(); }
    return new Microsoft.AspNetCore.Http.HttpResults.RazorComponentResult<Alife.Client.Android.Backend.Components.WindowApp>(new { Id = id });
});
app.MapRazorComponents<Alife.Client.Android.Backend.Components.App>()
    .AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Alife.Client.Components.Routes).Assembly);
app.Run();
