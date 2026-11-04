using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avas.Core.Audio;
using Avas.Core.Domain;

namespace Avas.Core.IO;

public static class ProjectCodec
{
    public const int MaximumBytes = 80 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Clone<T>(T value)
    {
        return JsonSerializer.Deserialize<T>(Json(value), Options)
            ?? throw new ArgumentException("不能复制空对象");
    }

    public static byte[] Encode(Project project)
    {
        var issues = Validation.Check(project);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("\n", issues));
        var bytes = Encoding.UTF8.GetBytes(Json(project));
        if (bytes.Length > MaximumBytes)
            throw new ArgumentException("项目超过80MiB，请移除不需要的音频或快照");
        return bytes;
    }

    public static Project Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes)
            throw new ArgumentException("项目超过80MiB限制");
        Project project;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("Schema", out var schema) || schema.GetInt32() != 1)
                throw new ArgumentException("缺少Schema或版本不受支持；本软件仅支持格式1");
            ValidateShape(document.RootElement, typeof(Project), "项目");
            project = JsonSerializer.Deserialize<Project>(bytes, Options)
                ?? throw new ArgumentException("项目为空");
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"项目JSON损坏，位置{error.Path}：{error.Message}");
        }
        var issues = Validation.Check(project);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("\n", issues));
        ValidateAssets(project);
        foreach (var snapshot in project.Snapshots)
        {
            var captured = new Project { Settings = snapshot.Settings, Speakers = snapshot.Speakers, Listeners = snapshot.Listeners };
            if (InputHash(captured) != snapshot.InputHash)
                throw new ArgumentException($"快照 {snapshot.Name} 的输入哈希不一致，不能作为可追溯方案打开");
        }
        return project;
    }

    public static string InputHash(Project project)
    {
        var input = new
        {
            Model = Software.Model,
            project.Settings,
            Speakers = project.Speakers.OrderBy(x => x.Id).ToArray(),
            Listeners = project.Listeners.OrderBy(x => x.Id).ToArray()
        };
        return Hash(Encoding.UTF8.GetBytes(Json(input)));
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string SourceSummary(Project project)
    {
        var sources = project.Speakers.Where(x => x.Enabled)
            .Select(x => $"{x.Name}:{x.Origin.Label()}:{x.Source}");
        return string.Join(";", sources) + ";背景:" + project.Settings.BackgroundOrigin.Label() + ":" + project.Settings.BackgroundSource;
    }

    public static string Conditions(Settings settings)
    {
        return Json(new
        {
            settings.MinX,
            settings.MaxX,
            settings.MinY,
            settings.MaxY,
            settings.Step,
            settings.ReceiverHeight,
            settings.MinimumDistance,
            settings.VehicleLength,
            settings.VehicleWidth,
            settings.ExcludeVehicle,
            settings.SelectedHz,
            settings.AWeighted,
            settings.LevelTarget,
            settings.MarginTarget,
            settings.Background,
            settings.BackgroundOrigin,
            settings.BackgroundSource,
            BackgroundAudioHash = settings.BackgroundAudio?.Sha256
        });
    }

    private static void ValidateShape(JsonElement element, Type type, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException(path + "必须为对象，不能为null");
        foreach (var property in type.GetProperties().Where(x => x.CanWrite))
        {
            var fieldPath = path + "." + property.Name;
            if (!element.TryGetProperty(property.Name, out var value))
                throw new ArgumentException(fieldPath + "缺失；格式1不自动补默认值");
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (property.PropertyType == typeof(AudioAsset))
                    continue;
                throw new ArgumentException(fieldPath + "不能为null");
            }
            var fieldType = property.PropertyType;
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (value.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException(fieldPath + "必须为数组");
                var itemType = fieldType.GetGenericArguments()[0];
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    ValidateShape(item, itemType, fieldPath + "[" + index++ + "]");
            }
            else if (fieldType == typeof(Settings) || fieldType == typeof(AudioAsset))
                ValidateShape(value, fieldType, fieldPath);
        }
    }

    private static void ValidateAssets(Project project)
    {
        var assets = project.Speakers.Select(x => x.Audio).Append(project.Settings.BackgroundAudio)
            .Concat(project.Snapshots.SelectMany(x => x.Speakers.Select(s => s.Audio).Append(x.Settings.BackgroundAudio)));
        foreach (var asset in assets.Where(x => x is not null).Cast<AudioAsset>())
        {
            byte[] data;
            try
            {
                data = Convert.FromBase64String(asset.Data);
            }
            catch (FormatException)
            {
                throw new ArgumentException($"音频 {asset.Name} 的内嵌数据不是有效Base64");
            }
            if (Hash(data) != asset.Sha256)
                throw new ArgumentException($"音频 {asset.Name} 校验和不一致，文件可能损坏");
            var decoded = WaveCodec.Read(data);
            if (decoded.Rate != asset.Rate || decoded.Channels != asset.Channels
                || Math.Abs(decoded.Duration - asset.Duration) > 0.001)
                throw new ArgumentException($"音频 {asset.Name} 的采样信息与数据不一致");
        }
    }
}
