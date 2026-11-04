using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blower.Core.Domain;

namespace Blower.Core.IO;

public static class ProjectCodec
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };

    public static string Encode(Project project)
    {
        Validation.RequireValid(project);
        return JsonSerializer.Serialize(project, Options);
    }

    public static Project Decode(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            throw new InvalidDataException("项目超过4MiB上限");
        }
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 32 });
            SchemaGuard.Inspect(document.RootElement);
            if (!document.RootElement.TryGetProperty("schema", out var schema)
                || schema.GetInt32() != Software.Schema)
            {
                throw new InvalidDataException("项目格式版本缺失或不支持；需要schema=1");
            }
            foreach (var name in new[] { "id", "name", "settings", "orders", "modes", "plans", "checks", "audit", "source" })
            {
                if (!document.RootElement.TryGetProperty(name, out var element)
                    || element.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidDataException("项目缺少必填字段：" + name);
                }
            }
            var project = JsonSerializer.Deserialize<Project>(json, Options)
                ?? throw new InvalidDataException("项目内容为空");
            CheckNullMembers(project);
            Validation.RequireValid(project);
            return project;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or NullReferenceException)
        {
            throw new InvalidDataException("项目JSON损坏或字段类型错误：" + error.Message, error);
        }
    }

    private static void CheckNullMembers(Project project)
    {
        if (project.Orders.Any(x => x is null)
            || project.Modes.Any(x => x is null)
            || project.Plans.Any(x => x is null || x.Gears is null || x.Gears.Any(g => g is null))
            || project.Checks.Any(x => x is null)
            || project.Audit.Any(x => x is null))
        {
            throw new InvalidDataException("对象列表含null，无法建立完整引用关系");
        }
    }

    public static Project Clone(Project project)
    {
        return JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project, Options), Options)!;
    }

    public static string Hash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    public static string InputHash(Project project)
    {
        var data = new { project.Settings, project.Orders, project.Modes, project.Plans };
        return Hash(JsonSerializer.Serialize(data, Options));
    }
}
