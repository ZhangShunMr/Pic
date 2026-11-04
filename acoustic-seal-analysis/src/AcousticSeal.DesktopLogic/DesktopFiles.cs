using System.Security.Cryptography;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.DesktopLogic;

public sealed class DesktopFiles
{
    private readonly ProjectCodec codec = new();
    public string Path { get; private set; } = "";
    public string OpenNotes { get; private set; } = "";
    private string? expectedHash;

    public AcousticProject Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var bytes = Read(full);
        var decoded = codec.Decode(bytes);
        var separate = decoded.Migrated || full.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        Path = separate ? "" : full;
        expectedHash = separate ? null : Hash(bytes);
        OpenNotes = string.Join("；", decoded.Notes) + (separate ? "；旧格式或备份请另存为新的.sealproj，原文件保持不变。" : "");
        return decoded.Project;
    }
    public void Reset()
    {
        Path = "";
        expectedHash = null;
    }
    public SaveReceipt Save(AcousticProject project, string path, bool overwriteConfirmed)
    {
        var full = System.IO.Path.GetFullPath(path);
        var bytes = codec.Encode(project);
        if (bytes.Length > Software.MaximumFileBytes) throw new DomainValidationException("项目", "文件超过12MiB");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        using var fileLock = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var exists = File.Exists(full);
        var actual = exists ? Hash(Read(full)) : null;
        var samePath = string.Equals(full, Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (samePath && expectedHash != actual) throw new ProjectConflictException();
        if (!samePath && exists && !overwriteConfirmed) throw new IOException("目标已存在，必须确认覆盖或改用新文件名。");
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (exists) File.Copy(full, full + ".bak", true);
            File.Move(temporary, full, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        Path = full;
        expectedHash = Hash(bytes);
        return new SaveReceipt(full, exists ? full + ".bak" : "", expectedHash, bytes.Length);
    }
    public static void Export(string path, byte[] bytes)
    {
        var full = System.IO.Path.GetFullPath(path);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static byte[] Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > Software.MaximumFileBytes) throw new DomainValidationException("文件", "文件超过12MiB，未读取");
        return File.ReadAllBytes(path);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
