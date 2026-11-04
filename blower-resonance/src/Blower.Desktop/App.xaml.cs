using System.Windows;

namespace Blower.Desktop;

public partial class App : Application
{
    public bool StartMainWindow { get; set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (StartMainWindow)
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }
}
