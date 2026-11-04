using System.Text;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Application;

public sealed class DocumentFile
{
    public string? PathName { get; private set; }
    public string? Revision { get; private set; }
    public string SavedContentHash { get; private set; } = "";

    public Project Open(string path)
    {
        var full = Path.GetFullPath(path);
        var bytes = ReadLimited(full, ProjectCodec.MaximumFileBytes);
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var project = ProjectCodec.Parse(text);
        PathName = full;
        Revision = HashBytes(bytes);
        SavedContentHash = ProjectCodec.Hash(ProjectCodec.Serialize(project));
        return project;
    }

    public void Detach()
    {
        PathName = null;
        Revision = null;
        SavedContentHash = "";
    }

    public bool IsDirty(Project project)
    {
        return ProjectCodec.Hash(ProjectCodec.Serialize(project)) != SavedContentHash;
    }

    public string Save(Project project, string path, bool allowOverwrite)
    {
        var full = Path.GetFullPath(path);
        var content = ProjectCodec.Serialize(project);
        var data = new UTF8Encoding(false).GetBytes(content);
        if (data.Length > ProjectCodec.MaximumFileBytes)
        {
            throw new InvalidDataException("项目超过8 MiB，未写入。请拆分频谱。");
        }
        var isCurrent = PathName is not null && string.Equals(full, PathName,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var exists = File.Exists(full);
        if (isCurrent && Revision is not null)
        {
            if (!exists || HashBytes(ReadLimited(full, ProjectCodec.MaximumFileBytes)) != Revision)
            {
                throw new InvalidDataException("磁盘文件已被外部修改、移动或删除，保存已拒绝。请另存为新路径，或重新打开核对。");
            }
        }
        else if (exists && !allowOverwrite)
        {
            throw new InvalidDataException("目标文件已存在，未覆盖。请选择新路径或确认覆盖。");
        }
        var directory = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("目标目录不存在，请选择有效目录。");
        }
        var temporary = Path.Combine(directory, ".motor-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(true);
            }
            if (exists)
            {
                File.Copy(full, full + ".bak", true);
            }
            File.Move(temporary, full, exists);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        PathName = full;
        Revision = HashBytes(data);
        SavedContentHash = ProjectCodec.Hash(content);
        return full;
    }

    public static byte[] ReadLimited(string path, int maximumBytes)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximumBytes)
        {
            throw new InvalidDataException($"文件超过允许大小 {maximumBytes} 字节，未读取。");
        }
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > maximumBytes)
            {
                throw new InvalidDataException("文件读取期间超过大小限制。");
            }
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string HashBytes(byte[] data)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
    }
}
