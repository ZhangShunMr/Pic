using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrimContact.Core.Domain;

namespace TrimContact.Core.IO;

public static class ProjectCodec
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(DesignProject project)
    {
        ProjectValidation.Require(project);
        return JsonSerializer.Serialize(project, Options);
    }

    public static DesignProject Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new InvalidDataException("项目文件超过2MB限制");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RequireMembers(document.RootElement, typeof(DesignProject), "项目");
            var project = JsonSerializer.Deserialize<DesignProject>(json, Options)
                ?? throw new InvalidDataException("项目内容为空");
            ProjectValidation.Require(project);
            return project;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("JSON格式错误：" + error.Message, error);
        }
    }

    private static void RequireMembers(JsonElement element, Type type, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (Nullable.GetUnderlyingType(type) is not null)
                return;
            throw new InvalidDataException(path + "不能为null");
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (element.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(path + "应为数组");
            int index = 0;
            foreach (var child in element.EnumerateArray())
                RequireMembers(child, type.GenericTypeArguments[0], path + $"[{index++}]");
            return;
        }
        if (type.Namespace != typeof(DesignProject).Namespace || type.IsEnum)
            return;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(path + "应为对象");
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!element.TryGetProperty(name, out var child))
                throw new InvalidDataException(path + "/" + name + "缺失；不自动填充未知旧版本");
            RequireMembers(child, property.PropertyType, path + "/" + name);
        }
        var names = new HashSet<string>();
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name))
                throw new InvalidDataException(path + "/" + property.Name + "重复字段");
    }

    public static DesignProject Clone(DesignProject project) => JsonSerializer.Deserialize<DesignProject>(
        JsonSerializer.Serialize(project, Options), Options)!;

    public static string FileHash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public static string InputHash(DesignProject project)
    {
        var input = new
        {
            project.Panel,
            project.Settings,
            project.Variants,
            project.Loads,
            project.Origin,
            project.Source
        };
        return FileHash(JsonSerializer.Serialize(input, Options));
    }
}
