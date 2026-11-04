using System.Windows;
using Cabin.Desktop.Views;

namespace Cabin.Desktop;

public partial class App : Application
{
    public bool StartMainWindow { get; set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!StartMainWindow)
            return;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
