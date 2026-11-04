using System.Text;
using TrimContact.Core.Domain;

namespace TrimContact.Core.IO;

public sealed record StoredProject(Guid Id, string Name, string Revision, string Origin,
    bool Archived, DateTimeOffset Modified, string? Error);

public sealed record OpenedProject(DesignProject Project, string Hash, string Path);

public sealed class ProjectStore
{
    private readonly string directory;
    private static readonly object FileGate = new();

    public ProjectStore(string directory)
    {
        this.directory = System.IO.Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    public string FilePath(Guid id) => System.IO.Path.Combine(directory, id.ToString("D") + ".trimproj");

    public IReadOnlyList<StoredProject> List()
    {
        var items = new List<StoredProject>();
        lock (FileGate)
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.trimproj").Order())
            {
                if (!Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out var id))
                    continue;
                try
                {
                    var p = Read(path);
                    items.Add(new(id, p.Name, p.Revision, p.Origin.Label(), p.Archived, p.ModifiedUtc, null));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    items.Add(new(id, "文件不可读取", "", "", false, DateTimeOffset.MinValue, error.Message));
                }
            }
        }
        return items.OrderByDescending(p => p.Modified).ToArray();
    }

    private static string ReadText(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > ProjectCodec.MaxBytes)
            throw new InvalidDataException("项目文件超过2MB限制");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static DesignProject Read(string path) => ProjectCodec.Parse(ReadText(path));

    public OpenedProject Open(Guid id, bool backup = false)
    {
        lock (FileGate)
        {
            var main = FilePath(id);
            var path = main + (backup ? ".bak" : "");
            var text = ReadText(path);
            var p = ProjectCodec.Parse(text);
            if (p.Id != id)
                throw new InvalidDataException("文件名ID与项目内容不一致");
            var hash = File.Exists(main) ? ProjectCodec.FileHash(ReadText(main)) : null;
            return new(p, hash ?? "", path);
        }
    }

    public OpenedProject Save(DesignProject project, string? expectedHash)
    {
        var text = ProjectCodec.Serialize(project);
        if (Encoding.UTF8.GetByteCount(text) > ProjectCodec.MaxBytes)
            throw new InvalidDataException("项目超过2MB，请精简操作记录或方案后保存");
        var path = FilePath(project.Id);
        lock (FileGate)
        {
            if (File.Exists(path))
            {
                if (expectedHash is null)
                    throw new InvalidDataException("项目库已存在相同ID；请打开磁盘版继续或复制为新项目");
                if (ProjectCodec.FileHash(ReadText(path)) != expectedHash)
                    throw new InvalidDataException("磁盘文件已变化，已阻止覆盖；请下载当前副本后重新打开磁盘版");
            }
            else if (expectedHash is not null)
                throw new InvalidDataException("原磁盘文件已被移走，已阻止静默重建；请复制为新项目");
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Copy(path, path + ".bak", true);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            return new(ProjectCodec.Clone(project), ProjectCodec.FileHash(text), path);
        }
    }
}
