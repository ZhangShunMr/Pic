using System.Text;
using Blower.Core.Domain;

namespace Blower.Core.IO;

public sealed record StoredProject(string Id, string Name, DateTimeOffset Modified, int Revision, bool Archived, string Status);
public sealed record OpenedProject(Project Project, string Token, string Path);
public sealed record SavedProject(string Token, string Path);

public sealed class ProjectStore
{
    private readonly string root;
    private static readonly object Gate = new();

    public ProjectStore(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    private string PathFor(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidDataException("项目标识必须为32位十六进制；拒绝路径字符");
        }
        return Path.Combine(root, id + ".blowerproj");
    }

    public IReadOnlyList<StoredProject> List()
    {
        var result = new List<StoredProject>();
        lock (Gate)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.blowerproj"))
            {
                try
                {
                    var project = ProjectCodec.Decode(File.ReadAllText(file, Encoding.UTF8));
                    result.Add(new(project.Id, project.Name, project.Modified, project.Revision, project.Archived, "可打开"));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    result.Add(new(Path.GetFileNameWithoutExtension(file), "损坏文件", File.GetLastWriteTimeUtc(file), 0, false, error.Message));
                }
            }
        }
        return result.OrderByDescending(x => x.Modified).ToList();
    }

    public OpenedProject Open(string id, bool backup = false)
    {
        lock (Gate)
        {
            var path = PathFor(id);
            var actual = backup ? path + ".bak" : path;
            var content = File.ReadAllText(actual, Encoding.UTF8);
            var project = ProjectCodec.Decode(content);
            if (project.Id != id)
            {
                throw new InvalidDataException("文件标识与内容不符；请通过项目导入检查");
            }
            var token = File.Exists(path) ? ProjectCodec.Hash(File.ReadAllText(path, Encoding.UTF8)) : "";
            return new(project, token, actual);
        }
    }

    public SavedProject Save(Project project, string expectedToken)
    {
        var content = ProjectCodec.Encode(project);
        var path = PathFor(project.Id);
        lock (Gate)
        {
            var exists = File.Exists(path);
            var current = exists ? ProjectCodec.Hash(File.ReadAllText(path, Encoding.UTF8)) : "";
            if (current != expectedToken)
            {
                throw new IOException("项目库中此项目已被其他会话修改；请先下载当前草稿，重新打开后比较或复制另存");
            }
            var temporary = path + "." + Software.Id() + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                if (exists)
                {
                    File.Copy(path, path + ".bak", true);
                }
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            return new(ProjectCodec.Hash(content), path);
        }
    }
}
