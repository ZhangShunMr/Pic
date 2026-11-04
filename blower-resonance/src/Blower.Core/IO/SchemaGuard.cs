using System.Text.Json;

namespace Blower.Core.IO;

public static class SchemaGuard
{
    private static void Require(JsonElement element, string path, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(path + "须为对象");
        }
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                throw new InvalidDataException(path + "." + name + "缺失或为null");
            }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new InvalidDataException(path + "包含重复字段：" + property.Name);
            }
        }
    }

    private static void Each(JsonElement root, string name, Action<JsonElement, string> inspect)
    {
        var list = root.GetProperty(name);
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(name + "须为数组");
        }
        var index = 0;
        foreach (var element in list.EnumerateArray())
        {
            inspect(element, name + "[" + index + "]");
            index++;
        }
    }

    public static void Inspect(JsonElement root)
    {
        Require(root, "project", "schema", "id", "name", "origin", "source", "settings",
            "orders", "modes", "plans", "checks", "audit", "revision", "created", "modified", "archived");
        Require(root.GetProperty("settings"), "settings", "blades", "minRpm", "maxRpm",
            "absoluteBandHz", "relativeBandPercent", "useDamping", "speedToleranceRpm",
            "minimumGapRpm", "scanStepRpm", "scanWindowRpm", "shiftWeight");
        Each(root, "orders", (item, path) =>
        {
            Require(item, path, "id", "name", "kind", "multiplier", "enabled", "evidence");
        });
        Each(root, "modes", (item, path) =>
        {
            Require(item, path, "id", "name", "frequencyHz", "extraBandHz", "enabled", "origin", "source", "location");
            if (!item.TryGetProperty("dampingRatio", out _))
            {
                throw new InvalidDataException(path + ".dampingRatio缺失；未知阻尼请明确写null");
            }
        });
        Each(root, "plans", (item, path) =>
        {
            Require(item, path, "id", "name", "notes", "locked", "gears");
            Each(item, "gears", (gear, gearPath) =>
            {
                Require(gear, path + "." + gearPath, "id", "name", "rpm", "fixed", "notes");
            });
        });
        Each(root, "checks", (item, path) =>
        {
            Require(item, path, "id", "key", "planId", "gearId", "modeId", "orderId",
                "title", "method", "owner", "evidence", "state", "updated");
        });
        Each(root, "audit", (item, path) =>
        {
            Require(item, path, "time", "action", "detail", "revision");
        });
    }
}
