using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Cabin.Core.IO;
using Cabin.Desktop.Views;

namespace Cabin.Desktop.Services;

public sealed class DesktopServices : IDesktopServices
{
    private Window Owner => Application.Current.MainWindow;

    public string? OpenProjectPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开座舱项目",
            Filter = "座舱项目 (*.cabin.json)|*.cabin.json",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? SaveProjectPath(string currentPath, string suggested)
    {
        var safe = string.Concat(suggested.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Title = "保存座舱项目",
            Filter = "座舱项目 (*.cabin.json)|*.cabin.json",
            DefaultExt = ".cabin.json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrEmpty(currentPath) ? safe + ".cabin.json" : Path.GetFileName(currentPath)
        };
        if (!string.IsNullOrEmpty(currentPath))
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? OpenCsvPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入点位CSV（先预检，确认后替换）",
            Filter = "UTF-8 CSV (*.csv)|*.csv",
            CheckFileExists = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ExportPath(string label, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出" + label,
            Filter = label + " (*" + extension + ")|*" + extension,
            FileName = label + extension,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public int SaveChanges(string name)
    {
        var answer = MessageBox.Show(Owner, "项目“" + name + "”有未保存内容。是否保存后继续？\n选择“否”放弃更改；“取消”返回。",
            "未保存内容", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer switch
        {
            MessageBoxResult.Yes => 1,
            MessageBoxResult.No => 2,
            _ => 0
        };
    }

    public int SelectExample()
    {
        var dialog = new ExampleWindow { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Choice : 0;
    }

    public bool ConfirmImport(CsvPreview preview)
    {
        var dialog = new ImportWindow(preview) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    public void Error(string message)
    {
        MessageBox.Show(Owner, message, "请检查输入或文件", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ExportFieldImage()
    {
        if (Owner is not MainWindow main)
            return;
        var visual = main.FieldVisual;
        var width = (int)Math.Ceiling(visual.ActualWidth);
        var height = (int)Math.Ceiling(visual.ActualHeight);
        if (width < 1 || height < 1)
            throw new InvalidOperationException("请先进入空间剖面工作区并更新剖面");
        var target = ExportPath("声场剖面", ".png");
        if (target is null)
            return;
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(target);
        encoder.Save(stream);
        MessageBox.Show(Owner, "已导出当前WPF声场画布：\n" + target, "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
