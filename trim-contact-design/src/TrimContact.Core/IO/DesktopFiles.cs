using System.Text;
using TrimContact.Core.Domain;

namespace TrimContact.Core.IO;

public static class DesktopFiles
{
    private static readonly object Gate = new();

    public static OpenedProject Open(string path)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("项目不存在，请重新选择文件。", path);
        if (info.Length > ProjectCodec.MaxBytes)
            throw new InvalidDataException("项目超过2MB限制");
        var text = File.ReadAllText(path, Encoding.UTF8);
        return new(ProjectCodec.Parse(text), ProjectCodec.FileHash(text), path);
    }

    public static OpenedProject Save(string path, DesignProject project, string? expectedHash,
        bool overwriteConfirmed = false)
    {
        path = Path.GetFullPath(path);
        var text = ProjectCodec.Serialize(project);
        if (Encoding.UTF8.GetByteCount(text) > ProjectCodec.MaxBytes)
            throw new InvalidDataException("项目超过2MB限制，请精简操作记录");
        lock (Gate)
        {
            if (File.Exists(path))
            {
                if (expectedHash is not null)
                {
                    if (Open(path).Hash != expectedHash)
                        throw new InvalidDataException("磁盘文件已变化，已阻止覆盖。请另存当前副本后重新打开磁盘文件。");
                }
                else if (!overwriteConfirmed)
                    throw new InvalidDataException("目标已存在，需要明确确认覆盖");
            }
            else if (expectedHash is not null)
                throw new InvalidDataException("原文件已移走，请另存为新文件");
            AtomicWrite(path, text, true);
        }
        return new(ProjectCodec.Clone(project), ProjectCodec.FileHash(text), path);
    }

    public static void AtomicWrite(string path, string text, bool backup = false)
    {
        path = Path.GetFullPath(path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(true));
            if (backup && File.Exists(path))
                File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static string ReadCsv(string path)
    {
        if (new FileInfo(path).Length > ProjectCodec.MaxBytes)
            throw new InvalidDataException("CSV超过2MB限制");
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
