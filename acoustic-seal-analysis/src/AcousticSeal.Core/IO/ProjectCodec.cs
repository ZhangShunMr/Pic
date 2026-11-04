using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.IO;

public sealed record DecodedProject(AcousticProject Project, bool Migrated, List<string> Notes);

public sealed class ProjectCodec
{
    public static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly ProjectValidator validator = new();

    public byte[] Encode(AcousticProject project)
    {
        validator.Ensure(project);
        return JsonSerializer.SerializeToUtf8Bytes(project, Options);
    }

    public DecodedProject Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > Software.MaximumFileBytes)
        {
            throw new DomainValidationException("file", "项目文件须为1字节至12MiB");
        }

        string content;
        try
        {
            content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new DomainValidationException("file.encoding", "文件不是有效UTF-8，请保留原文件");
        }

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
            InspectDuplicates(document.RootElement, "project");
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
            {
                throw new DomainValidationException("schemaVersion", "缺少整数格式版本");
            }

            if (number == 1)
            {
                var migrated = LegacyMigration.Convert(document.RootElement);
                validator.Ensure(migrated.Project);
                return migrated;
            }

            if (number != Software.SchemaVersion)
            {
                throw new DomainValidationException("schemaVersion", "不支持此版本，不修改原文件");
            }

            foreach (var required in new[] { "id", "name", "createdAt", "updatedAt", "revision", "status", "locations", "conditions", "trials", "pairs", "settings", "audit", "reviews" })
            {
                if (!document.RootElement.TryGetProperty(required, out _))
                {
                    throw new DomainValidationException(required, "项目缺少必要字段");
                }
            }

            InspectRequiredObjects(document.RootElement);
            var project = JsonSerializer.Deserialize<AcousticProject>(content, Options)
                ?? throw new DomainValidationException("project", "项目内容为空");
            validator.Ensure(project);
            return new DecodedProject(project, false, []);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new DomainValidationException("file.structure", "项目字段缺失或类型不匹配：" + error.Message);
        }
        catch (JsonException error)
        {
            throw new DomainValidationException(error.Path ?? "json", "JSON格式或类型错误：" + error.Message);
        }
    }

    public static T Clone<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return JsonSerializer.Deserialize<T>(bytes, Options)!;
    }

    public static string Fingerprint(AcousticProject project)
    {
        var copy = Clone(project);
        copy.Audit = [];
        copy.Reviews = [];
        copy.Revision = 0;
        copy.UpdatedAt = copy.CreatedAt;
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(copy, Options)));
    }

    private static void InspectDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new DomainValidationException(path, "重复JSON字段：" + property.Name);
                }

                InspectDuplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                InspectDuplicates(item, path + "[" + index++ + "]");
            }
        }
    }

    private static void InspectRequiredObjects(JsonElement root)
    {
        var requirements = new Dictionary<string, string[]>
        {
            ["locations"] = ["id", "name"],
            ["conditions"] = ["id", "name"],
            ["trials"] = ["id", "name", "kind", "conditionId", "locationIds", "timestamp", "source", "sourceNote", "bands"],
            ["pairs"] = ["id", "baselineId", "targetId", "purpose"],
            ["audit"] = ["id", "time", "revision", "action", "entity", "detail", "inputHash"],
            ["reviews"] = ["id", "time", "inputRevision", "reviewer", "validPairs", "blockedPairs", "blockingReasons", "settingsJson", "inputHash"]
        };
        foreach (var (collection, names) in requirements)
        {
            var array = root.GetProperty(collection);
            if (array.ValueKind != JsonValueKind.Array)
            {
                throw new DomainValidationException(collection, "必要对象集合须为数组");
            }

            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                RequireProperties(item, names, collection + "[" + index++ + "]");
            }
        }

        var settings = new AnalysisSettings().Describe().Select(setting => JsonNamingPolicy.CamelCase.ConvertName(setting.Key)).ToList();
        settings.Add("requireSampleRateMatch");
        settings.Add("requireSynchronizationMatch");
        RequireProperties(root.GetProperty("settings"), settings, "settings");
    }

    private static void RequireProperties(JsonElement element, IEnumerable<string> names, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DomainValidationException(path, "对象缺失或类型无效");
        }

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out _))
            {
                throw new DomainValidationException(path + "." + name, "缺少必要字段，不自动生成标识或猜测来源");
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 64,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new OffsetDateConverter());
        return options;
    }
}

public sealed class OffsetDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null || !Regex.IsMatch(text, "(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var value))
        {
            throw new JsonException("日期时间必须带时区，例如2026-01-01T10:00:00+08:00");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }
}
