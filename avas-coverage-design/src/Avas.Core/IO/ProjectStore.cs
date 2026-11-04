using Avas.Core.Domain;

namespace Avas.Core.IO;

public sealed record StoredProject(string Id, string Name, string FileName, DateTimeOffset Modified, long Size, string Hash, bool HasBackup, string? Error);
public sealed record SaveReceipt(string Path, string Hash, bool BackupCreated);

public sealed class ProjectStore
{
    private static readonly object Gate = new();
    public string DirectoryPath { get; }

    public ProjectStore(string path)
    {
        DirectoryPath = Path.GetFullPath(path);
        Directory.CreateDirectory(DirectoryPath);
    }

    public List<StoredProject> List()
    {
        lock (Gate)
        {
            return Directory.EnumerateFiles(DirectoryPath, "*.avasproj").Select(path =>
            {
                var info = new FileInfo(path);
                try
                {
                    var data = File.ReadAllBytes(path);
                    var project = ProjectCodec.Decode(data);
                    return new StoredProject(project.Id.ToString(), project.Name, info.Name,
                        info.LastWriteTimeUtc, info.Length, ProjectCodec.Hash(data), File.Exists(path + ".bak"), null);
                }
                catch (Exception error) when (error is ArgumentException or IOException or FormatException)
                {
                    return new StoredProject(Path.GetFileNameWithoutExtension(path), info.Name, info.Name,
                        info.LastWriteTimeUtc, info.Length, "", File.Exists(path + ".bak"), error.Message);
                }
            }).OrderByDescending(x => x.Modified).ToList();
        }
    }

    public (Project Project, string Hash) Open(Guid id, bool backup = false)
    {
        lock (Gate)
        {
            var path = FilePath(id) + (backup ? ".bak" : "");
            var data = File.ReadAllBytes(path);
            return (ProjectCodec.Decode(data), ProjectCodec.Hash(data));
        }
    }

    public SaveReceipt Save(Project project, string? expectedHash)
    {
        var bytes = ProjectCodec.Encode(project);
        lock (Gate)
        {
            var path = FilePath(project.Id);
            var exists = File.Exists(path);
            if (exists)
            {
                var currentHash = ProjectCodec.Hash(File.ReadAllBytes(path));
                if (expectedHash is null || currentHash != expectedHash)
                    throw new IOException("同标识项目已存在或被其他会话修改；请先打开磁盘版，或复制为独立项目后保存。");
            }
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                if (exists)
                    File.Copy(path, path + ".bak", true);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            return new SaveReceipt(path, ProjectCodec.Hash(bytes), exists);
        }
    }

    private string FilePath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".avasproj");
}
