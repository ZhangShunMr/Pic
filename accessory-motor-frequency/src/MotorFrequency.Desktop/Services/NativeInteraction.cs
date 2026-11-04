using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using MotorFrequency.Application;

namespace MotorFrequency.Desktop.Services;

public sealed class NativeInteraction(Window owner) : IDesktopInteraction
{
    public string? OpenProjectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开项目或将备份恢复为副本",
            Filter = "电机频率项目及备份|*.motorproj;*.motorproj.bak|电机频率项目|*.motorproj|备份|*.motorproj.bak",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? OpenSpectrumFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "读取UTF-8频谱CSV",
            Filter = "频谱CSV|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string suggestedName, string extension, string description)
    {
        var dialog = new SaveFileDialog
        {
            Title = "选择本机保存位置",
            FileName = suggestedName,
            DefaultExt = extension,
            AddExtension = true,
            Filter = description + "|*" + extension,
            OverwritePrompt = false,
            CheckPathExists = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public LeaveChoice ConfirmLeave(string reason)
    {
        var answer = MessageBox.Show(owner, reason + "\n\n是：保存后继续；否：放弃修改继续；取消：返回。",
            "当前有未保存工作", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer switch
        {
            MessageBoxResult.Yes => LeaveChoice.Save,
            MessageBoxResult.No => LeaveChoice.Discard,
            _ => LeaveChoice.Cancel
        };
    }

    public bool Confirm(string message)
    {
        return MessageBox.Show(owner, message, "确认操作", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public void RevealFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("成果文件已移动或删除。", path);
        }
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/select,");
        start.ArgumentList.Add(Path.GetFullPath(path));
        Process.Start(start);
    }
}
