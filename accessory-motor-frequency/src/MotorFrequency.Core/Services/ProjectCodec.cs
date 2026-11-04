using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class ProjectCodec
{
    public const int MaximumFileBytes = 8 * 1024 * 1024;
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static string Serialize(Project project)
    {
        ProjectValidator.ThrowIfInvalid(project);
        return JsonSerializer.Serialize(project, Options);
    }

    public static Project Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumFileBytes)
        {
            throw new InvalidDataException("项目超过 8 MiB，未载入。请拆分频谱数据。");
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (!root.TryGetProperty("formatVersion", out var version) || version.GetInt32() != Software.FormatVersion)
            {
                throw new InvalidDataException("formatVersion 缺失或不支持；仅支持版本 1，原文件未修改。");
            }
            string[] required = ["id", "name", "motor", "customOrders", "bands", "spectra", "scenarios", "history", "review"];
            foreach (var property in required)
            {
                if (!root.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidDataException($"项目必需字段 {property} 缺失或为空。");
                }
            }
            RequiredSchema.Validate(root);
            var project = JsonSerializer.Deserialize<Project>(text, Options)
                ?? throw new InvalidDataException("项目不是有效对象。");
            ProjectValidator.ThrowIfInvalid(project);
            return project;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("项目 JSON 损坏或字段类型错误：" + ex.Message, ex);
        }
    }

    public static T Clone<T>(T value)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)
            ?? throw new InvalidOperationException("无法复制对象。");
    }

    public static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public static Project Duplicate(Project project)
    {
        var copy = Clone(project);
        copy.Id = Guid.NewGuid();
        copy.Name = project.Name[..Math.Min(project.Name.Length, 96)] + "（副本）";
        copy.Archived = false;
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.ModifiedAt = copy.CreatedAt;
        copy.History.Add(new AuditEvent { Action = "复制项目", Detail = "来源项目 " + project.Id });
        if (copy.History.Count > 2000)
        {
            copy.History.RemoveRange(0, copy.History.Count - 2000);
        }
        ProjectValidator.ThrowIfInvalid(copy);
        return copy;
    }
}
