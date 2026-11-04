using System.Collections.Concurrent;
using System.Text;

namespace Blower.App;

public sealed record DownloadFile(byte[] Bytes, string Mime, string Name, DateTimeOffset Expires);

public sealed class DownloadVault
{
    private readonly ConcurrentDictionary<string, DownloadFile> files = new();

    public string Add(string content, string mime, string name)
    {
        foreach (var pair in files.Where(x => x.Value.Expires < DateTimeOffset.UtcNow))
        {
            files.TryRemove(pair.Key, out _);
        }
        if (files.Count >= 80)
        {
            var oldest = files.MinBy(x => x.Value.Expires);
            files.TryRemove(oldest.Key, out _);
        }
        var id = Guid.NewGuid().ToString("N");
        var safeName = string.Concat(name.Where(x => !Path.GetInvalidFileNameChars().Contains(x)));
        var bytes = Encoding.UTF8.GetBytes((mime == "text/csv" ? "\uFEFF" : "") + content);
        files[id] = new(bytes, mime + "; charset=utf-8", safeName, DateTimeOffset.UtcNow.AddMinutes(20));
        return "/download/" + id;
    }

    public DownloadFile? Get(string id)
    {
        if (!files.TryGetValue(id, out var file) || file.Expires < DateTimeOffset.UtcNow)
        {
            return null;
        }
        return file;
    }
}
