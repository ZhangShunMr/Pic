using System.IO;
using System.Text;
using Blower.Core.Domain;
using Blower.Core.IO;

namespace Blower.Desktop.Services;

public sealed record DiskProject(Project Project, string Path, string Token);

public static class DesktopFiles
{
    public static DiskProject Open(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > ProjectCodec.MaximumBytes)
        {
            throw new InvalidDataException("文件超过 4MiB 上限");
        }
        var content = File.ReadAllText(path, Encoding.UTF8);
        var project = ProjectCodec.Decode(content);
        return new(project, Path.GetFullPath(path), ProjectCodec.Hash(content));
    }

    public static string Save(Project project, string path, string? expectedToken)
    {
        var content = ProjectCodec.Encode(project);
        if (Encoding.UTF8.GetByteCount(content) > ProjectCodec.MaximumBytes)
        {
            throw new InvalidDataException("序列化项目超过 4MiB，请拆分项目或精简备注");
        }
        var current = File.Exists(path)
            ? ProjectCodec.Hash(File.ReadAllText(path, Encoding.UTF8)) : "";
        if (expectedToken != null && expectedToken != current)
        {
            throw new IOException("磁盘文件已被其他程序修改或删除。请另存为新文件，再打开原文件核对差异。");
        }
        WriteAtomic(path, content, false, true);
        var reopened = Open(path);
        if (ProjectCodec.InputHash(reopened.Project) != ProjectCodec.InputHash(project))
        {
            throw new IOException("保存后重开核对失败，请保留当前会话并另存");
        }
        return reopened.Token;
    }

    public static void WriteAtomic(string path, string content, bool bom, bool backup = false)
    {
        var full = Path.GetFullPath(path);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (bom)
                {
                    stream.Write(Encoding.UTF8.GetPreamble());
                }
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(true);
            }
            if (backup && File.Exists(full))
            {
                File.Copy(full, full + ".bak", true);
            }
            File.Move(temporary, full, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static string ReadCsv(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024)
        {
            throw new InvalidDataException("CSV 超过 1MiB 预检上限");
        }
        return File.ReadAllText(path, new UTF8Encoding(false, true));
    }
}
