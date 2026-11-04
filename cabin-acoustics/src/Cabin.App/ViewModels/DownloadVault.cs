namespace Cabin.App.ViewModels;

public sealed record DownloadItem(string Name, string Mime, byte[] Bytes, DateTimeOffset Created);

public sealed class DownloadVault
{
    private readonly Dictionary<Guid, DownloadItem> items = [];
    private readonly object gate = new();

    public string Add(string name, string mime, byte[] bytes)
    {
        lock (gate)
        {
            foreach (var old in items.Where(x => x.Value.Created < DateTimeOffset.UtcNow.AddMinutes(-20)).Select(x => x.Key).ToList())
                items.Remove(old);
            while (items.Count >= 30)
                items.Remove(items.OrderBy(x => x.Value.Created).First().Key);
            var id = Guid.NewGuid();
            items[id] = new(name, mime, bytes, DateTimeOffset.UtcNow);
            return "/download/" + id;
        }
    }

    public DownloadItem? Get(Guid id)
    {
        lock (gate)
        {
            return items.TryGetValue(id, out var value) && value.Created > DateTimeOffset.UtcNow.AddMinutes(-20) ? value : null;
        }
    }
}
