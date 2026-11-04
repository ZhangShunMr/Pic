using Avas.App;
using Avas.App.Components;
using Avas.App.ViewModels;
using Avas.Core.IO;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var portText = builder.Configuration["port"] ?? "8786";
if (!int.TryParse(portText, out var port) || port is < 1024 or > 65535)
    throw new ArgumentException("--port 必须为1024至65535的端口");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton(new ProjectStore(builder.Configuration["data-directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "Data")));
builder.Services.AddSingleton<DownloadVault>();
builder.Services.AddScoped<WorkspaceViewModel>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/download/{key}", (string key, DownloadVault vault) =>
{
    var item = vault.Get(key);
    return item is null ? Results.NotFound("输出已过期，请回到软件重新生成。")
        : Results.File(item.Data, item.Mime, item.Name, enableRangeProcessing: true);
});
app.MapGet("/health", () => new { software = "AvasCoverage", version = "1.0", status = "ready" });
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
