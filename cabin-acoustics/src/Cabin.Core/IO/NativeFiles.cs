using System.Text;
using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public static class NativeFiles
{
    public static Project Open(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new IOException("项目文件不存在：" + path);
        if (info.Length is <= 0 or > Software.MaximumBytes)
            throw new ArgumentException("项目文件必须为非空且不超过2MiB");
        return ProjectCodec.Decode(File.ReadAllBytes(path));
    }

    public static void Save(string path, Project project)
    {
        var bytes = ProjectCodec.Encode(project);
        _ = ProjectCodec.Decode(bytes);
        AtomicWrite(path, bytes);
    }

    public static void WriteText(string path, string text)
    {
        AtomicWrite(path, new UTF8Encoding(true).GetBytes(text));
    }

    public static string ReadCsv(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 400_000)
            throw new ArgumentException("点位CSV不存在或超过400000字节");
        return File.ReadAllText(path, new UTF8Encoding(false, true));
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var absolute = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolute)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("请先建立保存目录：" + directory);
        var temporary = absolute + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, absolute, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
