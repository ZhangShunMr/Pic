using System.Security.Cryptography;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.IO;

public sealed record StoredProject(AcousticProject Project, string ETag, string Path, bool FromBackup = false);
public sealed record SaveReceipt(string Path, string BackupPath, string ETag, long Bytes);
public sealed record ProjectCard(Guid Id, string Name, long Revision, DateTimeOffset Modified, string Path, bool HasBackup, string Error);

public sealed class ProjectConflictException : Exception
{
    public ProjectConflictException()
        : base("磁盘项目已被其他会话修改或删除，已阻止覆盖。请另存副本，或重新打开磁盘版本后再编辑。")
    {
    }
}

public sealed class ProjectStore
{
    private readonly string directory;
    private readonly ProjectCodec codec = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public ProjectStore(string directory)
    {
        this.directory = System.IO.Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    public string DirectoryPath => directory;

    public async Task<List<ProjectCard>> ListAsync(CancellationToken cancellation = default)
    {
        var cards = new List<ProjectCard>();
        foreach (var file in Directory.EnumerateFiles(directory, "*" + Software.Extension))
        {
            cancellation.ThrowIfCancellationRequested();
            var filename = System.IO.Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(filename, "N", out var id))
            {
                continue;
            }

            try
            {
                var stored = await LoadAsync(id, cancellation);
                cards.Add(new ProjectCard(id, stored.Project.Name, stored.Project.Revision,
                    stored.Project.UpdatedAt, file, File.Exists(file + ".bak"), ""));
            }
            catch (Exception error) when (error is IOException or DomainValidationException or UnauthorizedAccessException)
            {
                cards.Add(new ProjectCard(id, "无法读取的项目", 0, new DateTimeOffset(File.GetLastWriteTimeUtc(file)),
                    file, File.Exists(file + ".bak"), error.Message));
            }
        }

        return cards.OrderByDescending(card => card.Modified).ToList();
    }

    public async Task<StoredProject> LoadAsync(Guid id, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var file = PathFor(id);
            var bytes = await ReadBoundedAsync(file, cancellation);
            var decoded = codec.Decode(bytes);
            if (decoded.Project.Id != id)
            {
                throw new DomainValidationException("file.id", "文件名与内嵌项目标识不一致，请通过导入项目另存");
            }

            return new StoredProject(decoded.Project, Hash(bytes), file);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<StoredProject> LoadBackupAsync(Guid id, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var file = PathFor(id);
            var backup = file + ".bak";
            var bytes = await ReadBoundedAsync(backup, cancellation);
            var decoded = codec.Decode(bytes);
            if (decoded.Project.Id != id)
            {
                throw new DomainValidationException("backup.id", "备份项目标识与文件名不一致");
            }

            var current = File.Exists(file) ? Hash(await ReadBoundedAsync(file, cancellation)) : "";
            return new StoredProject(decoded.Project, current, backup, true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SaveReceipt> SaveAsync(AcousticProject project, string? expectedETag, CancellationToken cancellation = default)
    {
        var bytes = codec.Encode(project);
        if (bytes.Length > Software.MaximumFileBytes)
        {
            throw new DomainValidationException("file", "项目超过12MiB，请减少记录或分项目保存");
        }

        await gate.WaitAsync(cancellation);
        string? temporary = null;
        try
        {
            var file = PathFor(project.Id);
            // Share.None coordinates cooperating application instances before the
            // ETag check. The persistent lock file avoids unlink/recreate races.
            using var fileLock = new FileStream(file + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var exists = File.Exists(file);
            var actual = exists ? Hash(await ReadBoundedAsync(file, cancellation)) : null;
            var expected = string.IsNullOrEmpty(expectedETag) ? null : expectedETag;
            if (actual != expected)
            {
                throw new ProjectConflictException();
            }

            temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellation);
                await stream.FlushAsync(cancellation);
                stream.Flush(flushToDisk: true);
            }

            if (exists)
            {
                File.Copy(file, file + ".bak", overwrite: true);
            }

            File.Move(temporary, file, overwrite: true);
            temporary = null;
            return new SaveReceipt(file, exists ? file + ".bak" : "", Hash(bytes), bytes.Length);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            gate.Release();
        }
    }

    public string PathFor(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainValidationException("project.id", "项目标识不能为空");
        }

        return System.IO.Path.Combine(directory, id.ToString("N") + Software.Extension);
    }

    private static async Task<byte[]> ReadBoundedAsync(string file, CancellationToken cancellation)
    {
        var info = new FileInfo(file);
        if (info.Length > Software.MaximumFileBytes)
        {
            throw new DomainValidationException("file", "文件超过12MiB，拒绝读取");
        }

        return await File.ReadAllBytesAsync(file, cancellation);
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
