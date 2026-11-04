namespace Avas.App;

public sealed record DownloadItem(byte[] Data, string Mime, string Name, DateTimeOffset Expires);

public sealed class DownloadVault
{
    private readonly Dictionary<string, DownloadItem> items = new();
    private readonly object gate = new();

    public string Add(byte[] data, string mime, string name)
    {
        lock (gate)
        {
            foreach (var expiredKey in items.Where(x => x.Value.Expires < DateTimeOffset.UtcNow).Select(x => x.Key).ToArray())
                items.Remove(expiredKey);
            if (data.Length > 100 * 1024 * 1024)
                throw new ArgumentException("单项输出不能超过100MiB");
            while (items.Count > 0 && items.Values.Sum(x => (long)x.Data.Length) + data.Length > 160 * 1024 * 1024)
                items.Remove(items.MinBy(x => x.Value.Expires).Key);
            var key = Guid.NewGuid().ToString("N");
            items[key] = new DownloadItem(data, mime, name, DateTimeOffset.UtcNow.AddMinutes(10));
            return "/download/" + key;
        }
    }

    public DownloadItem? Get(string key)
    {
        lock (gate)
        {
            if (!items.TryGetValue(key, out var item) || item.Expires < DateTimeOffset.UtcNow)
                return null;
            return item;
        }
    }
}
