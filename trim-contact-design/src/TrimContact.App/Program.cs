using System.Globalization;
using TrimContact.App;
using TrimContact.App.Components;
using TrimContact.App.ViewModels;
using TrimContact.Core.IO;

var options = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length || args[i] is not ("--port" or "--data-directory"))
        throw new ArgumentException("参数：--port 8775 --data-directory 可写目录");
    options[args[i]] = args[i + 1];
}
var port = options.TryGetValue("--port", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 8775;
if (port is < 1024 or > 65535)
    throw new ArgumentException("端口须为1024至65535");
var directory = options.GetValueOrDefault("--data-directory") ?? Path.Combine(AppContext.BaseDirectory, "Data");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseStaticWebAssets();
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options =>
{
    options.DetailedErrors = false;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
}).AddHubOptions(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024);
builder.Services.AddSingleton(new ProjectStore(directory));
builder.Services.AddSingleton<DownloadVault>();
builder.Services.AddScoped<WorkspaceViewModel>();
var app = builder.Build();
app.UseStaticFiles();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapGet("/download/{id}", (string id, DownloadVault vault) =>
{
    var file = vault.Get(id);
    return file is null ? Results.NotFound("下载链接已过期，请重新导出")
        : Results.File(file.Bytes, file.ContentType, file.Name);
});
app.MapGet("/health", () => Results.Text("TrimContact V1.0 ready"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
