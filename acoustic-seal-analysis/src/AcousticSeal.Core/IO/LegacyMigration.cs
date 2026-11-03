using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.IO;

public static class LegacyMigration
{
    public static DecodedProject Convert(JsonElement root)
    {
        var project = new AcousticProject
        {
            Id = Id(Text(root, "id")),
            Name = Text(root, "name"),
            Owner = Text(root, "owner"),
            Description = Text(root, "description"),
            CreatedAt = Date(root, "createdAt"),
            UpdatedAt = Date(root, "updatedAt"),
            Revision = root.GetProperty("revision").GetInt64(),
            Status = Text(root, "status") == "archived" ? ProjectStatus.Archived : ProjectStatus.Active
        };
        foreach (var site in Array(root, "sites"))
        {
            project.Locations.Add(new SealLocation
            {
                Id = Id(Text(site, "id")),
                Name = Text(site, "name"),
                Zone = Text(site, "zone"),
                Material = Text(site, "material"),
                Procedure = Text(site, "procedure"),
                Notes = Text(site, "notes")
            });
        }

        foreach (var condition in Array(root, "conditions"))
        {
            project.Conditions.Add(new MeasurementCondition
            {
                Id = Id(Text(condition, "id")),
                Name = Text(condition, "name"),
                Vehicle = Text(condition, "vehicle"),
                Microphone = Text(condition, "microphone"),
                Excitation = Text(condition, "excitation"),
                Surface = Text(condition, "surface"),
                Gear = Text(condition, "gear"),
                Weighting = Text(condition, "weighting"),
                TimeDefinition = Text(condition, "timeWeighting"),
                BandType = Text(condition, "bandType"),
                Instrument = Text(condition, "instrument"),
                Calibration = Text(condition, "calibration"),
                Unit = Text(condition, "unit"),
                Speed = Number(condition, "speed"),
                Rpm = Number(condition, "rpm"),
                Temperature = Number(condition, "temperature"),
                Duration = Number(condition, "duration"),
                NonOverlappingBands = condition.GetProperty("independentBands").GetBoolean(),
                Notes = Text(condition, "notes")
            });
        }

        foreach (var trial in Array(root, "trials"))
        {
            var kind = Text(trial, "type") switch
            {
                "baseline" => TrialKind.Baseline,
                "single" => TrialKind.Single,
                "combination" => TrialKind.Combination,
                "recovery" => TrialKind.Recovery,
                _ => throw new DomainValidationException("legacy.type", "旧试验类型无效")
            };
            var source = Text(trial, "sourceKind") switch
            {
                "simulation" => SourceKind.Simulation,
                "measured" => SourceKind.Measured,
                "manual" => SourceKind.Manual,
                _ => throw new DomainValidationException("legacy.sourceKind", "旧来源类别无效")
            };
            var bands = new List<BandMeasurement>();
            foreach (var band in trial.GetProperty("bands").EnumerateObject())
            {
                if (!double.TryParse(band.Name, NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency))
                {
                    throw new DomainValidationException("legacy.bands", "频率不是数值");
                }

                bands.Add(new BandMeasurement
                {
                    Frequency = frequency,
                    Level = band.Value.ValueKind == JsonValueKind.Null ? null : band.Value.GetDouble()
                });
            }

            project.Trials.Add(new AcousticTrial
            {
                Id = Id(Text(trial, "id")),
                Name = Text(trial, "name"),
                Kind = kind,
                ConditionId = Id(Text(trial, "conditionId")),
                LocationIds = Array(trial, "siteIds").Select(item => Id(item.GetString()!)).ToList(),
                Timestamp = Date(trial, "timestamp"),
                RepeatGroup = Text(trial, "repeatGroup"),
                Source = source,
                SourceNote = Text(trial, "sourceNote"),
                Operator = Text(trial, "operator"),
                Notes = Text(trial, "notes"),
                Bands = bands
            });
        }

        foreach (var pair in Array(root, "pairs"))
        {
            var purpose = Text(pair, "purpose") switch
            {
                "effect" => PairPurpose.Effect,
                "drift" => PairPurpose.Drift,
                "recovery" => PairPurpose.Recovery,
                _ => throw new DomainValidationException("legacy.purpose", "旧配对用途无效")
            };
            project.Pairs.Add(new TrialPair
            {
                Id = Id(Text(pair, "id")),
                BaselineId = Id(Text(pair, "baselineId")),
                TargetId = Id(Text(pair, "targetId")),
                Purpose = purpose,
                Notes = Text(pair, "notes")
            });
        }

        var parameters = root.GetProperty("parameters");
        project.Settings.SpeedTolerance = Number(parameters, "speedTolerance") ?? 0.5;
        project.Settings.RpmTolerance = Number(parameters, "rpmTolerance") ?? 20;
        project.Settings.TemperatureTolerance = Number(parameters, "temperatureTolerance") ?? 2;
        project.Settings.DurationTolerance = Number(parameters, "durationTolerance") ?? 0.01;
        project.Settings.DriftThreshold = Number(parameters, "driftThreshold") ?? 1;
        project.Settings.RecoveryThreshold = Number(parameters, "recoveryThreshold") ?? 1;
        project.Settings.RepeatabilityThreshold = Number(parameters, "repeatabilityThreshold") ?? 1;
        foreach (var entry in Array(root, "audit"))
        {
            project.Audit.Add(new AuditEntry
            {
                Time = Date(entry, "time"),
                Action = Text(entry, "action"),
                Detail = Text(entry, "detail"),
                Entity = "legacy",
                Revision = 0
            });
        }

        project.Revision++;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.Audit.Add(new AuditEntry
        {
            Action = "导入旧格式v1",
            Entity = project.Id.ToString(),
            Revision = project.Revision,
            Detail = "将旧字符串标识确定性映射为GUID；保留试验来源和频带；旧文件未修改"
        });
        return new DecodedProject(project, true,
        [
            "检测到旧版.aslp.json，已在内存中迁移至schema2；原文件不变，请另存.sealproj。",
            "旧字符串标识映射为稳定GUID，原位置、工况、试验、配对和来源保留。",
            "新增采样率、同步定义、准备与恢复工艺字段留空，不捏造缺少的记录。"
        ]);
    }

    public static Guid Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("legacy.id", "旧标识不能为空");
        }

        return Guid.TryParse(value, out var guid) ? guid : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("AcousticSeal/v1/" + value))[..16]);
    }

    private static string Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : "";
    }

    private static double? Number(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDouble() : null;
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new DomainValidationException("legacy." + name, "旧文件缺少必要集合");
        }

        return value.EnumerateArray();
    }

    private static DateTimeOffset Date(JsonElement element, string name)
    {
        var text = Text(element, name);
        if (text.Length < 6 || !(text.EndsWith('Z') || text[^6] is '+' or '-') ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new DomainValidationException("legacy." + name, "旧时间格式无效或缺少时区");
        }

        return value;
    }
}
