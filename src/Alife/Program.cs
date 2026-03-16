using System.Reflection;
using System.Text;
using Alife;
using Alife.Components;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
Assembly.Load("Alife.Function"); //官方插件没有直接依赖，所以不会自动加载，需要手动

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAntDesign();
builder.Services.AddSingleton<StorageSystem>();
builder.Services.AddSingleton<ConfigurationSystem>();
builder.Services.AddSingleton<PluginSystem>();
builder.Services.AddSingleton<CharacterSystem>();
builder.Services.AddSingleton<ChatActivitySystem>();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
