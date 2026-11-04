using System.Reflection;
using System.Text.Json;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class RequiredSchema
{
    public static void Validate(JsonElement root)
    {
        var issues = new List<string>();
        Visit(root, typeof(Project), "project", issues);
        if (issues.Count > 0)
        {
            throw new InvalidDataException("项目格式字段不完整：\n" + string.Join("\n", issues.Take(25)));
        }
    }

    private static void Visit(JsonElement element, Type type, string path, List<string> issues)
    {
        if (issues.Count >= 25) return;
        if (element.ValueKind == JsonValueKind.Null)
        {
            issues.Add(path + " 不能为 null。");
            return;
        }
        if (type == typeof(string) || type.IsValueType)
        {
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                issues.Add(path + " 必须为数组。");
                return;
            }
            var itemType = type.GetGenericArguments()[0];
            var index = 0;
            foreach (var child in element.EnumerateArray())
            {
                Visit(child, itemType, path + "/" + index, issues);
                index++;
                if (issues.Count >= 25) break;
            }
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(path + " 必须为对象。");
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!element.TryGetProperty(name, out var child))
            {
                issues.Add(path + "/" + name + " 缺失。版本 1 不静默填入默认值。");
                continue;
            }
            Visit(child, property.PropertyType, path + "/" + name, issues);
        }
    }
}
