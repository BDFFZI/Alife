using Alife;
using Alife.Components;

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

//活动自启
// CharacterSystem characterSystem = app.Services.GetRequiredService<CharacterSystem>();
// ChatActivitySystem chatActivitySystem = app.Services.GetRequiredService<ChatActivitySystem>();
// foreach (Character character in characterSystem.GetAllCharacters())
// {
//     if(character.)
// }
