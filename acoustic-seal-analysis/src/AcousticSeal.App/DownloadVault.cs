using System.Collections.Concurrent;
using AcousticSeal.Core.IO;

namespace AcousticSeal.App;

public sealed class DownloadVault
{
    private readonly ConcurrentDictionary<Guid, DownloadEntry> entries = new();

    public string Add(ExportDocument document)
    {
        Purge();
        if (document.Bytes.Length > 24 * 1024 * 1024)
        {
            throw new IOException("单个导出超过24MiB，请缩小项目范围");
        }

        while (entries.Count >= 20)
        {
            var oldest = entries.OrderBy(item => item.Value.Expires).FirstOrDefault();
            entries.TryRemove(oldest.Key, out _);
        }

        var id = Guid.NewGuid();
        entries[id] = new DownloadEntry(document, DateTimeOffset.UtcNow.AddMinutes(10));
        return "/download/" + id.ToString("N");
    }

    public ExportDocument? Find(Guid id)
    {
        Purge();
        return entries.TryGetValue(id, out var value) ? value.Document : null;
    }

    private void Purge()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, value) in entries)
        {
            if (value.Expires < now)
            {
                entries.TryRemove(id, out _);
            }
        }
    }

    private sealed record DownloadEntry(ExportDocument Document, DateTimeOffset Expires);
}
