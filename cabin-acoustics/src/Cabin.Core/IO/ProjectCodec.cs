using System.Text.Json;
using System.Text.Json.Serialization;
using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public static class ProjectCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24
    };

    public static byte[] Encode(Project project)
    {
        var issues = Validation.Check(project, false);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("；", issues.Select(x => x.Path + "：" + x.Message)));
        return JsonSerializer.SerializeToUtf8Bytes(project, Options);
    }

    public static Project Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > Software.MaximumBytes)
            throw new ArgumentException("项目应为非空JSON，且不超过2MiB");
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 24 });
            var root = document.RootElement;
            Require(root, "schema", "id", "name", "revision", "dataSource", "isDemo", "created", "modified", "model", "sources", "seats", "baselines", "reviews", "journal");
            if (root.GetProperty("schema").GetInt32() != Software.Schema)
                throw new ArgumentException("只支持项目格式版本1；未执行迁移");
            Require(root.GetProperty("model"), "length", "width", "height", "soundSpeed", "damping", "minimumFrequency", "maximumFrequency", "frequencyStep", "cutoff");
            foreach (var source in root.GetProperty("sources").EnumerateArray())
            {
                Require(source, "id", "name", "point", "amplitude", "phaseDegrees", "enabled");
                Require(source.GetProperty("point"), "x", "y", "z");
            }
            foreach (var seat in root.GetProperty("seats").EnumerateArray())
            {
                Require(seat, "id", "name", "point");
                Require(seat.GetProperty("point"), "x", "y", "z");
            }
            foreach (var baseline in root.GetProperty("baselines").EnumerateArray())
            {
                Require(baseline, "id", "name", "model", "sources", "seats", "isDemo", "dataSource");
                Require(baseline.GetProperty("model"), "length", "width", "height", "soundSpeed", "damping", "minimumFrequency", "maximumFrequency", "frequencyStep", "cutoff");
                foreach (var source in baseline.GetProperty("sources").EnumerateArray())
                {
                    Require(source, "id", "name", "point", "amplitude", "phaseDegrees", "enabled");
                    Require(source.GetProperty("point"), "x", "y", "z");
                }
                foreach (var seat in baseline.GetProperty("seats").EnumerateArray())
                {
                    Require(seat, "id", "name", "point");
                    Require(seat.GetProperty("point"), "x", "y", "z");
                }
            }
            foreach (var item in root.GetProperty("reviews").EnumerateArray())
                Require(item, "id", "target", "action", "owner", "result");
            foreach (var entry in root.GetProperty("journal").EnumerateArray())
                Require(entry, "time", "action", "detail");
            var project = JsonSerializer.Deserialize<Project>(bytes, Options) ?? throw new ArgumentException("项目内容为null");
            var issues = Validation.Check(project, false);
            if (issues.Count > 0)
                throw new ArgumentException(string.Join("；", issues.Select(x => x.Path + "：" + x.Message)));
            return project;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new ArgumentException("JSON项目结构或数值格式错误：" + error.Message);
        }
    }

    public static T Clone<T>(T value)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Options), Options)!;
    }

    private static void Require(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("预期JSON对象");
        var names = element.EnumerateObject().Select(x => x.Name).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new ArgumentException("JSON包含重复字段");
        foreach (var field in fields)
        {
            if (!element.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
                throw new ArgumentException("缺少字段：" + field);
        }
    }
}
