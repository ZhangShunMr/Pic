using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public sealed record StoredProject(Guid Id, string Name, string Revision, DateTimeOffset Modified, bool IsDemo, string Error);

public sealed class ProjectStore(string directory)
{
    private readonly object gate = new();
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public void Save(Project project)
    {
        var bytes = ProjectCodec.Encode(project);
        lock (gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var target = Path.Combine(DirectoryPath, project.Id + Software.Extension);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    public Project Open(Guid id)
    {
        lock (gate)
        {
            var path = Path.Combine(DirectoryPath, id + Software.Extension);
            if (!File.Exists(path))
                throw new ArgumentException("本机项目已不存在，请从项目文件导入");
            var info = new FileInfo(path);
            if (info.Length > Software.MaximumBytes)
                throw new ArgumentException("项目超过2MiB，拒绝读取");
            return ProjectCodec.Decode(File.ReadAllBytes(path));
        }
    }

    public List<StoredProject> List()
    {
        var result = new List<StoredProject>();
        lock (gate)
        {
            if (!Directory.Exists(DirectoryPath))
                return result;
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*" + Software.Extension))
            {
                var name = Path.GetFileName(path);
                if (!Guid.TryParse(name[..^Software.Extension.Length], out var id))
                    continue;
                try
                {
                    var project = Open(id);
                    result.Add(new(project.Id, project.Name, project.Revision, project.Modified, project.IsDemo, ""));
                }
                catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    result.Add(new(id, name, "?", File.GetLastWriteTimeUtc(path), false, error.Message));
                }
            }
        }
        return result.OrderByDescending(x => x.Modified).ToList();
    }

    public void Delete(Guid id)
    {
        lock (gate)
        {
            var path = Path.Combine(DirectoryPath, id + Software.Extension);
            if (!File.Exists(path))
                throw new ArgumentException("目标项目不存在");
            File.Delete(path);
        }
    }
}
