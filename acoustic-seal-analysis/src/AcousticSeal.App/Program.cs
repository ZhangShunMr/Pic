using System.Net;
using AcousticSeal.App;
using AcousticSeal.App.Components;
using AcousticSeal.App.ViewModels;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue<int?>("port") ?? 8765;
if (port is < 1024 or > 65535)
{
    throw new ArgumentException("端口允许1024至65535");
}

builder.WebHost.UseUrls("http://127.0.0.1:" + port);
var dataDirectory = builder.Configuration["data-directory"] ??
    Path.Combine(builder.Environment.ContentRootPath, "Data");
builder.Services.AddSingleton(new ProjectStore(dataDirectory));
builder.Services.AddSingleton<DownloadVault>();
builder.Services.AddScoped<WorkspaceViewModel>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options =>
{
    options.DetailedErrors = builder.Environment.IsDevelopment();
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
}).AddHubOptions(options => options.MaximumReceiveMessageSize = 128 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = Software.MaximumFileBytes);

var app = builder.Build();
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    var remote = context.Connection.RemoteIpAddress;
    if (host is not ("127.0.0.1" or "localhost") || remote is null || !IPAddress.IsLoopback(remote))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("仅允许本机回环访问");
        return;
    }

    var originText = context.Request.Headers.Origin.ToString();
    if (originText.Length > 0 && (!Uri.TryCreate(originText, UriKind.Absolute, out var origin) ||
        origin.Host != host || origin.Port != port || origin.Scheme != "http"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("拒绝跨站访问");
        return;
    }

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws://127.0.0.1:" + port + " ws://localhost:" + port + "; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    await next();
});
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Json(new
{
    name = Software.Name,
    version = Software.Version,
    schema = Software.SchemaVersion,
    status = "ready"
}));
app.MapGet("/download/{id:guid}", (Guid id, DownloadVault vault) =>
{
    var document = vault.Find(id);
    return document is null ? Results.NotFound("下载已过期，请重新准备导出") :
        Results.File(document.Bytes, document.ContentType, document.Filename);
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Logger.LogInformation("{Name} {Version}; project directory: {Directory}", Software.Name, Software.DisplayVersion, dataDirectory);
app.Run();
