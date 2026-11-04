using Microsoft.Win32;
using System.Windows;
using AcousticSeal.DesktopLogic;
namespace AcousticSeal.Desktop;
public sealed class NativeDialogs : IUserDialogs
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
    public string? SaveFile(string title, string filter, string filename)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = filename, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
    public bool Confirm(string title, string text) => MessageBox.Show(Application.Current.MainWindow, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public bool? SaveChanges(string text) => MessageBox.Show(Application.Current.MainWindow, text, "未保存工作", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    {
        MessageBoxResult.Yes => true,
        MessageBoxResult.No => false,
        _ => null
    };
    public void ShowError(string text) => MessageBox.Show(Application.Current.MainWindow, text, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
}
