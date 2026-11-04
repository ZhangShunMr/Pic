using System.IO;
using System.Windows;
using Avas.Core.IO;
using Avas.Workspace;

namespace Avas.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvasCoverage", "Projects");
        try
        {
            var services = new DesktopServices();
            var workspace = new WorkspaceViewModel(new ProjectStore(data), services);
            var window = new MainWindow(workspace);
            MainWindow = window;
            window.Show();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"无法建立本机项目库：{data}\n{error.Message}\n请检查当前用户目录的写入权限。", "启动未完成", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
