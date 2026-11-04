using System.IO;
using System.Windows;
using Microsoft.Win32;
using Blower.Core.Domain;

namespace Blower.Desktop.Services;

public sealed class DesktopDialogs : IDesktopDialogs
{
    public string? OpenProject()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开鼓风机项目",
            Filter = "鼓风机项目 (*.blowerproj)|*.blowerproj|备份文件 (*.bak)|*.bak",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveProject(string name)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存鼓风机项目",
            Filter = "鼓风机项目 (*.blowerproj)|*.blowerproj",
            FileName = SafeName(name),
            DefaultExt = ".blowerproj",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "读取 UTF-8 CSV 到预检区",
            Filter = "CSV (*.csv)|*.csv",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ExportPath(string name, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出成果",
            Filter = $"{extension.ToUpperInvariant()} 文件 (*.{extension})|*.{extension}",
            FileName = SafeName(name),
            DefaultExt = "." + extension,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string message)
    {
        return MessageBox.Show(message, Software.Name, MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public SaveDecision Unsaved(string message)
    {
        return MessageBox.Show(message, Software.Name, MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => SaveDecision.Save,
            MessageBoxResult.No => SaveDecision.Discard,
            _ => SaveDecision.Cancel
        };
    }

    private static string SafeName(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }
        return name.Length > 100 ? name[..100] : name;
    }
}

