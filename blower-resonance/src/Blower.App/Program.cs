using Blower.App;
using Blower.App.Components;
using Blower.App.ViewModels;
using Blower.Core.IO;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 5 * 1024 * 1024);
builder.Services.AddScoped<WorkspaceViewModel>();
builder.Services.AddSingleton(new ProjectStore(
    Environment.GetEnvironmentVariable("BLOWER_PROJECT_ROOT")
    ?? Path.Combine(builder.Environment.ContentRootPath, ".project-library")));
builder.Services.AddSingleton<DownloadVault>();
builder.Services.AddServerSideBlazor().AddCircuitOptions(options =>
{
    options.DetailedErrors = false;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 5 * 1024 * 1024);
var app = builder.Build();
app.UseExceptionHandler("/error");
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapGet("/error", () => Results.Problem("服务发生异常；刷新页面前请检查已保存项目。"));
app.MapGet("/download/{id}", (string id, DownloadVault vault) =>
{
    var file = vault.Get(id);
    return file == null ? Results.NotFound() : Results.File(file.Bytes, file.Mime, file.Name);
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
