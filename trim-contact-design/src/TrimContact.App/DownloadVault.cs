using System.Collections.Concurrent;
using System.Text;

namespace TrimContact.App;

public sealed record DownloadFile(byte[] Bytes, string ContentType, string Name, DateTimeOffset Expiry);

public sealed class DownloadVault
{
    private readonly ConcurrentDictionary<string, DownloadFile> files = new();

    public string Add(string text, string contentType, string name)
    {
        foreach (var stale in files.Where(p => p.Value.Expiry < DateTimeOffset.UtcNow))
            files.TryRemove(stale.Key, out _);
        if (files.Count >= 100)
            foreach (var old in files.OrderBy(p => p.Value.Expiry).Take(20))
                files.TryRemove(old.Key, out _);
        var id = Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes(text);
        if (contentType.StartsWith("text/csv", StringComparison.Ordinal))
            bytes = Encoding.UTF8.GetPreamble().Concat(bytes).ToArray();
        files[id] = new DownloadFile(bytes, contentType, name, DateTimeOffset.UtcNow.AddMinutes(30));
        return "/download/" + id;
    }

    public DownloadFile? Get(string id) => files.TryGetValue(id, out var file)
        && file.Expiry >= DateTimeOffset.UtcNow ? file : null;
}
