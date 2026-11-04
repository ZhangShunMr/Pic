using System.Windows;
using Microsoft.Win32;
using TrimContact.Desktop.ViewModels;

namespace TrimContact.Desktop;

public sealed class DesktopInteraction : IDesktopInteraction
{
    public string? OpenPath(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? SavePath(string filter, string suggested)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = suggested,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public bool Confirm(string message) => MessageBox.Show(Application.Current.MainWindow,
        message, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public UnsavedChoice Unsaved(string message) => MessageBox.Show(Application.Current.MainWindow,
        message + "\n是：保存；否：放弃；取消：继续编辑。", "未保存修改",
        MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => UnsavedChoice.Save,
            MessageBoxResult.No => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel
        };

    public void Error(string message) => MessageBox.Show(Application.Current.MainWindow,
        message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Information(string message) => MessageBox.Show(Application.Current.MainWindow,
        message, "接触设计工作台", MessageBoxButton.OK, MessageBoxImage.Information);
}
