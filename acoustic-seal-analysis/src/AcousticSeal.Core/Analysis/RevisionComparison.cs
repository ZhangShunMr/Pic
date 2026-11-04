using System.Globalization;
using System.Text.Json;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.Core.Analysis;

public enum ChangeKind
{
    Added,
    Removed,
    Modified
}

public sealed record RevisionChange(
    ChangeKind Kind,
    string Collection,
    string EntityId,
    string Path,
    string Before,
    string After);

public sealed class RevisionComparisonResult
{
    public string SoftwareVersion { get; init; } = Software.Version;
    public Guid LeftProjectId { get; init; }
    public Guid RightProjectId { get; init; }
    public string LeftName { get; init; } = "";
    public string RightName { get; init; } = "";
    public long LeftRevision { get; init; }
    public long RightRevision { get; init; }
    public string LeftHash { get; init; } = "";
    public string RightHash { get; init; } = "";
    public List<string> Notes { get; init; } = [];
    public List<RevisionChange> Changes { get; init; } = [];
    public int AddedCount => Changes.Count(change => change.Kind == ChangeKind.Added);
    public int RemovedCount => Changes.Count(change => change.Kind == ChangeKind.Removed);
    public int ModifiedCount => Changes.Count(change => change.Kind == ChangeKind.Modified);
}

public sealed class RevisionComparison
{
    public RevisionComparisonResult Compare(AcousticProject left, AcousticProject right)
    {
        var validator = new ProjectValidator();
        validator.Ensure(left);
        validator.Ensure(right);
        using var before = JsonDocument.Parse(JsonSerializer.Serialize(left, ProjectCodec.Options));
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(right, ProjectCodec.Options));
        var changes = new List<RevisionChange>();
        var ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "audit", "reviews", "createdAt", "updatedAt", "softwareVersion", "revision", "id", "schemaVersion"
        };
        foreach (var property in before.RootElement.EnumerateObject())
        {
            if (!ignored.Contains(property.Name))
            {
                Walk(property.Value, after.RootElement.GetProperty(property.Name), property.Name, "project", "", changes);
            }
        }

        var notes = new List<string>
        {
            "只按对象GUID和频率键核对，不按名称猜测对象对应关系。",
            "这里是两份已录入项目内容的变化，不是封堵效果或物理路径贡献。",
            "审计与复核记录只比较数量，详细历史在各项目文件内查看；时间、修订和软件版本列为身份信息。"
        };
        if (left.Id != right.Id)
        {
            notes.Add("两侧项目标识不同：只核对仍具有相同GUID的对象，不能断言属于同一次试验版本。");
        }

        if (left.Audit.Count != right.Audit.Count || left.Reviews.Count != right.Reviews.Count)
        {
            notes.Add($"审计数量{left.Audit.Count}→{right.Audit.Count}；复核数量{left.Reviews.Count}→{right.Reviews.Count}。");
        }

        return new RevisionComparisonResult
        {
            LeftProjectId = left.Id,
            RightProjectId = right.Id,
            LeftName = left.Name,
            RightName = right.Name,
            LeftRevision = left.Revision,
            RightRevision = right.Revision,
            LeftHash = ProjectCodec.Fingerprint(left),
            RightHash = ProjectCodec.Fingerprint(right),
            Notes = notes,
            Changes = changes
        };
    }

    private static void Walk(JsonElement left, JsonElement right, string path, string collection, string entityId, List<RevisionChange> changes)
    {
        if (left.ValueKind != right.ValueKind)
        {
            Add(ChangeKind.Modified, left, right, path, collection, entityId, changes);
            return;
        }

        if (left.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in left.EnumerateObject())
            {
                if (right.TryGetProperty(property.Name, out var other))
                {
                    Walk(property.Value, other, path + "." + property.Name, collection, entityId, changes);
                }
                else
                {
                    changes.Add(new RevisionChange(ChangeKind.Removed, collection, entityId,
                        path + "." + property.Name, Show(property.Value), "未提供"));
                }
            }

            foreach (var property in right.EnumerateObject())
            {
                if (!left.TryGetProperty(property.Name, out _))
                {
                    changes.Add(new RevisionChange(ChangeKind.Added, collection, entityId,
                        path + "." + property.Name, "未提供", Show(property.Value)));
                }
            }

            return;
        }

        if (left.ValueKind == JsonValueKind.Array)
        {
            var a = left.EnumerateArray().ToList();
            var b = right.EnumerateArray().ToList();
            var keyed = a.Concat(b).All(item => item.ValueKind == JsonValueKind.Object && Key(item) is not null);
            if (keyed)
            {
                var leftMap = a.ToDictionary(item => Key(item)!);
                var rightMap = b.ToDictionary(item => Key(item)!);
                foreach (var key in leftMap.Keys.Union(rightMap.Keys).Order(StringComparer.Ordinal))
                {
                    var foundLeft = leftMap.TryGetValue(key, out var first);
                    var foundRight = rightMap.TryGetValue(key, out var second);
                    var location = path + "[" + key + "]";
                    if (!foundLeft)
                    {
                        changes.Add(new RevisionChange(ChangeKind.Added, path, key, location, "未提供", Show(second)));
                    }
                    else if (!foundRight)
                    {
                        changes.Add(new RevisionChange(ChangeKind.Removed, path, key, location, Show(first), "未提供"));
                    }
                    else
                    {
                        Walk(first, second, location, path, key, changes);
                    }
                }
            }
            else
            {
                var leftValues = a.Select(item => item.GetRawText()).Order(StringComparer.Ordinal);
                var rightValues = b.Select(item => item.GetRawText()).Order(StringComparer.Ordinal);
                if (!leftValues.SequenceEqual(rightValues))
                {
                    Add(ChangeKind.Modified, left, right, path, collection, entityId, changes);
                }
            }

            return;
        }

        if (left.GetRawText() != right.GetRawText())
        {
            Add(ChangeKind.Modified, left, right, path, collection, entityId, changes);
        }
    }

    private static string? Key(JsonElement value)
    {
        if (value.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }

        if (value.TryGetProperty("frequency", out var frequency))
        {
            return frequency.GetDouble().ToString("G17", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static void Add(ChangeKind kind, JsonElement before, JsonElement after, string path,
        string collection, string entityId, List<RevisionChange> changes)
    {
        changes.Add(new RevisionChange(kind, collection, entityId, path, Show(before), Show(after)));
    }

    private static string Show(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "缺失(null)",
            JsonValueKind.String => value.GetString() ?? "",
            _ => value.GetRawText()
        };
    }
}
