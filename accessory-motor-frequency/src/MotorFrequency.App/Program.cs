using MotorFrequency.App.Components;
using MotorFrequency.App.ViewModels;
using MotorFrequency.Core.Services;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration["port"] ?? "8774";
if (!int.TryParse(port, out var number) || number is < 1024 or > 65535)
{
    throw new ArgumentException("--port 需为 1024～65535 的整数。");
}
builder.WebHost.UseStaticWebAssets();
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
var directory = builder.Configuration["data-directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "Data");
builder.Services.AddSingleton(new ProjectStore(directory));
builder.Services.AddScoped<WorkspaceViewModel>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddServerSideBlazor().AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024);
var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapGet("/health", () => Results.Json(new { software = "MotorFrequency", version = "V1.0", status = "ready" }));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
