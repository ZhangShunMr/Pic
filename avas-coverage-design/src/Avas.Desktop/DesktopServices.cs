using System.IO;
using System.Media;
using System.Windows;
using Microsoft.Win32;
using Avas.Workspace;

namespace Avas.Desktop;

public sealed class DesktopServices : IDesktopServices, IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "AvasCoverage-" + Guid.NewGuid().ToString("N"));
    private SoundPlayer? player;
    private int audioRevision;
    public string LastReadName { get; private set; } = "";

    public bool Confirm(string title, string message) => MessageBox.Show(Application.Current.MainWindow, message, title,
        MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public byte[]? ReadFile(string kind)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择本机输入文件",
            CheckFileExists = true,
            Multiselect = false,
            Filter = kind switch
            {
                "project" => "AVAS 项目 (*.avasproj)|*.avasproj|项目备份 (*.bak)|*.bak",
                "wave" => "PCM16 WAV (*.wav)|*.wav",
                _ => "UTF-8 CSV (*.csv)|*.csv"
            }
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return null;
        var size = new FileInfo(dialog.FileName).Length;
        var maximum = kind switch { "project" => 80L * 1024 * 1024, "wave" => 10L * 1024 * 1024, _ => 1024L * 1024 };
        if (size > maximum)
            throw new ArgumentException($"所选文件超过 {maximum / (1024 * 1024)} MiB 限制。");
        LastReadName = Path.GetFileName(dialog.FileName);
        return File.ReadAllBytes(dialog.FileName);
    }

    public string WriteExport(byte[] bytes, string suggestedName)
    {
        var extension = Path.GetExtension(suggestedName);
        var dialog = new SaveFileDialog
        {
            Title = "选择成果保存位置",
            FileName = suggestedName,
            AddExtension = true,
            DefaultExt = extension,
            OverwritePrompt = true,
            Filter = $"{extension} 文件|*{extension}"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            throw new OperationCanceledException();
        var path = dialog.FileName;
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return path;
    }

    public string WriteTemporaryAudio(byte[] bytes)
    {
        StopAudio();
        Directory.CreateDirectory(temporaryDirectory);
        foreach (var prior in Directory.EnumerateFiles(temporaryDirectory, "*.wav"))
            File.Delete(prior);
        var path = Path.Combine(temporaryDirectory, $"audition-{++audioRevision}.wav");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void PlayAudio(string path)
    {
        StopAudio();
        player = new SoundPlayer(path);
        player.Load();
        player.Play();
    }

    public void StopAudio()
    {
        player?.Stop();
        player?.Dispose();
        player = null;
    }

    public void Dispose()
    {
        StopAudio();
        if (Directory.Exists(temporaryDirectory))
        {
            try { Directory.Delete(temporaryDirectory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
