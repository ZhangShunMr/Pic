using System.ComponentModel;
using System.Windows;
using AcousticSeal.DesktopLogic;
namespace AcousticSeal.Desktop;
public partial class MainWindow : Window
{
    private readonly Workspace workspace;
    public MainWindow() : this(new NativeDialogs()) { }
    public MainWindow(IUserDialogs dialogs)
    {
        InitializeComponent();
        workspace = new Workspace(dialogs);
        DataContext = workspace;
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Ensure the focused native editor transfers its last keystroke before the save decision.
        FocusManagerCommit();
        e.Cancel = !workspace.CanClose();
    }
    private void FocusManagerCommit()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }
}
