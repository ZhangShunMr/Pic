using System.Net;
using Cabin.App.Components;
using Cabin.App.ViewModels;
using Cabin.Core.Domain;
using Cabin.Core.IO;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue<int?>("port") ?? 8786;
if (port is < 1024 or > 65535)
    throw new ArgumentException("端口允许1024至65535");
builder.WebHost.UseStaticWebAssets();
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
var directory = builder.Configuration["data-directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "Data");
builder.Services.AddSingleton(new ProjectStore(directory));
builder.Services.AddScoped<WorkspaceViewModel>();
builder.Services.AddSingleton<DownloadVault>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 256 * 1024);
var app = builder.Build();
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    var remote = context.Connection.RemoteIpAddress;
    if (host is not ("127.0.0.1" or "localhost") || remote is null || !IPAddress.IsLoopback(remote))
    {
        context.Response.StatusCode = 403;
        return;
    }
    var originText = context.Request.Headers.Origin.ToString();
    if (originText.Length > 0 && (!Uri.TryCreate(originText, UriKind.Absolute, out var origin) ||
        origin.Host != host || origin.Port != port || origin.Scheme != "http"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Json(new { name = Software.Name, version = Software.Version, status = "ready" }));
app.MapGet("/download/{id:guid}", (Guid id, DownloadVault vault) =>
{
    var item = vault.Get(id);
    return item is null ? Results.NotFound("下载已过期，请重新生成") : Results.File(item.Bytes, item.Mime, item.Name);
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
