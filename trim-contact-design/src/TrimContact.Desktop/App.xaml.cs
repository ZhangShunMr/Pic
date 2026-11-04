using System.Windows;

namespace TrimContact.Desktop;

public partial class App : Application
{
    public bool StartMainWindow { get; set; } = true;
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("发生未预期错误，请另存项目并记录以下信息：\n" + args.Exception.Message,
                "接触设计工作台", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
        if (StartMainWindow) new MainWindow().Show();
    }
}
