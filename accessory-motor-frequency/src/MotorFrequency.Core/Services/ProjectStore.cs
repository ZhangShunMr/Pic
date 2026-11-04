using System.Text;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public sealed class ProjectStore
{
    private readonly string directory;
    private readonly object gate = new();
    public string DirectoryPath => directory;

    public ProjectStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    private string ProjectPath(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidDataException("项目标识不能为空。");
        }
        return Path.Combine(directory, id.ToString("N") + Software.Extension);
    }

    public StoreWrite Save(Project project, string? expectedRevision)
    {
        ProjectValidator.ThrowIfInvalid(project);
        lock (gate)
        {
            var path = ProjectPath(project.Id);
            var exists = File.Exists(path);
            if (exists)
            {
                var oldText = File.ReadAllText(path, Encoding.UTF8);
                var actualRevision = ProjectCodec.Hash(oldText);
                if (expectedRevision is null || actualRevision != expectedRevision)
                {
                    throw new InvalidDataException("磁盘项目已存在或被外部修改，未覆盖。请先从项目库打开核对，或复制为新项目。");
                }
            }
            else if (expectedRevision is not null)
            {
                throw new InvalidDataException("原项目文件已被移动或删除，未写入。请复制项目后保存。");
            }
            var text = ProjectCodec.Serialize(project);
            if (Encoding.UTF8.GetByteCount(text) > ProjectCodec.MaximumFileBytes)
            {
                throw new InvalidDataException("保存内容超过 8 MiB，未写入。");
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
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
            return new(path, ProjectCodec.Hash(text), exists);
        }
    }

    public StoreRead Open(Guid id)
    {
        lock (gate)
        {
            var path = ProjectPath(id);
            var text = ReadLimited(path);
            var project = ProjectCodec.Parse(text);
            if (project.Id != id)
            {
                throw new InvalidDataException("项目内容标识与文件名不一致，请通过导入入口核对。");
            }
            return new(project, ProjectCodec.Hash(text));
        }
    }

    public Project ReadBackupAsCopy(Guid id)
    {
        lock (gate)
        {
            var text = ReadLimited(ProjectPath(id) + ".bak");
            var project = ProjectCodec.Duplicate(ProjectCodec.Parse(text));
            project.Name += "（备份恢复）";
            project.History.Add(new AuditEvent { Action = "读取备份", Detail = "以新项目恢复，不覆盖原文件" });
            return project;
        }
    }

    public IReadOnlyList<ProjectSummary> List(out IReadOnlyList<string> warnings)
    {
        var result = new List<ProjectSummary>();
        var errors = new List<string>();
        lock (gate)
        {
            var files = Directory.EnumerateFiles(directory, "*" + Software.Extension).Take(501).ToList();
            if (files.Count > 500)
            {
                errors.Add("项目库超过 500 个文件，仅列出前 500 个；请归档到其他目录。");
            }
            foreach (var file in files.Take(500))
            {
                try
                {
                    var p = ProjectCodec.Parse(ReadLimited(file));
                    if (Path.GetFileName(file) != p.Id.ToString("N") + Software.Extension)
                    {
                        errors.Add(Path.GetFileName(file) + "：标识不匹配，未列入可打开项目。");
                        continue;
                    }
                    result.Add(new(p.Id, p.Name, p.Demonstration, p.Archived, p.ModifiedAt, file, File.Exists(file + ".bak")));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException)
                {
                    errors.Add(Path.GetFileName(file) + "：" + ex.Message);
                }
            }
        }
        warnings = errors;
        return result.OrderByDescending(x => x.ModifiedAt).ToList();
    }

    private static string ReadLimited(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("找不到项目文件。", path);
        }
        if (info.Length > ProjectCodec.MaximumFileBytes)
        {
            throw new InvalidDataException("项目文件超过 8 MiB。");
        }
        return File.ReadAllText(path, new UTF8Encoding(false, true));
    }
}
