namespace AcousticSeal.Core.Domain;

public sealed class AcousticProject
{
    public int SchemaVersion { get; set; } = Software.SchemaVersion;
    public string SoftwareVersion { get; set; } = Software.Version;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名声学试验项目";
    public string Owner { get; set; } = "";
    public string VehicleProgramme { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Revision { get; set; }
    public ProjectStatus Status { get; set; }
    public List<SealLocation> Locations { get; set; } = [];
    public List<MeasurementCondition> Conditions { get; set; } = [];
    public List<AcousticTrial> Trials { get; set; } = [];
    public List<TrialPair> Pairs { get; set; } = [];
    public AnalysisSettings Settings { get; set; } = new();
    public List<AuditEntry> Audit { get; set; } = [];
    public List<ReviewRecord> Reviews { get; set; } = [];

    public static AcousticProject Empty()
    {
        return new AcousticProject();
    }

    public string SourceSummary()
    {
        if (Trials.Count == 0)
        {
            return "尚无试验数据";
        }

        return string.Join(" · ", Trials.GroupBy(trial => trial.Source)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key.Text()} {group.Count()}"));
    }

    public string LocationNames(IEnumerable<Guid> identifiers)
    {
        var map = Locations.ToDictionary(location => location.Id);
        return string.Join(" + ", identifiers.Select(id =>
            map.TryGetValue(id, out var location) ? location.Name : "无效位置"));
    }

    public string ConditionName(Guid id)
    {
        return Conditions.FirstOrDefault(condition => condition.Id == id)?.Name ?? "未找到工况";
    }

    public string TrialName(Guid id)
    {
        return Trials.FirstOrDefault(trial => trial.Id == id)?.Name ?? "未找到试验";
    }
}

public sealed class SealLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Material { get; set; } = "";
    public string Extent { get; set; } = "";
    public string Procedure { get; set; } = "";
    public string RecoveryProcedure { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class MeasurementCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Microphone { get; set; } = "";
    public string Excitation { get; set; } = "";
    public string Surface { get; set; } = "";
    public string Gear { get; set; } = "";
    public string Weighting { get; set; } = "Z";
    public string TimeDefinition { get; set; } = "Leq";
    public string BandType { get; set; } = "octave";
    public string Instrument { get; set; } = "";
    public string Calibration { get; set; } = "";
    public string Unit { get; set; } = Software.LevelUnit;
    public double? Speed { get; set; }
    public double? Rpm { get; set; }
    public double? Temperature { get; set; }
    public double? Duration { get; set; }
    public double? SampleRate { get; set; }
    public string Synchronization { get; set; } = "";
    public bool NonOverlappingBands { get; set; }
    public string Notes { get; set; } = "";

    public IEnumerable<ConditionTextField> TextFields()
    {
        yield return new("vehicle", "车辆标识", Vehicle);
        yield return new("microphone", "测点及安装位置", Microphone);
        yield return new("excitation", "声源与激励", Excitation);
        yield return new("surface", "路面或试验场", Surface);
        yield return new("gear", "挡位", Gear);
        yield return new("weighting", "频率计权", Weighting);
        yield return new("timeDefinition", "时间计权及积分定义", TimeDefinition);
        yield return new("bandType", "频带制", BandType);
        yield return new("instrument", "仪器标识", Instrument);
        yield return new("calibration", "标定链路与状态", Calibration);
        yield return new("unit", "声级单位及参考", Unit);
    }
}

public sealed record ConditionTextField(string Key, string Label, string Value);

public sealed class AcousticTrial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public TrialKind Kind { get; set; }
    public Guid ConditionId { get; set; }
    public List<Guid> LocationIds { get; set; } = [];
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string RepeatGroup { get; set; } = "";
    public SourceKind Source { get; set; } = SourceKind.Manual;
    public string SourceNote { get; set; } = "";
    public string OriginalFile { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Preparation { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<BandMeasurement> Bands { get; set; } = [];

    public double? LevelAt(double frequency)
    {
        return Bands.FirstOrDefault(band => band.Frequency == frequency)?.Level;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ValidBandCount => Bands.Count(band => band.Level.HasValue);
    [System.Text.Json.Serialization.JsonIgnore]
    public int MissingBandCount => Bands.Count(band => !band.Level.HasValue);
}

public sealed class BandMeasurement
{
    public double Frequency { get; set; }
    public double? Level { get; set; }
    public string Note { get; set; } = "";
}

public sealed class TrialPair
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BaselineId { get; set; }
    public Guid TargetId { get; set; }
    public PairPurpose Purpose { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class AnalysisSettings
{
    public double SpeedTolerance { get; set; } = 0.5;
    public double RpmTolerance { get; set; } = 20;
    public double TemperatureTolerance { get; set; } = 2;
    public double DurationTolerance { get; set; } = 0.01;
    public double DriftThreshold { get; set; } = 1;
    public double RecoveryThreshold { get; set; } = 1;
    public double RepeatabilityThreshold { get; set; } = 1;
    public double MinimumEffectDisplay { get; set; }
    public bool RequireSampleRateMatch { get; set; }
    public bool RequireSynchronizationMatch { get; set; }

    public IEnumerable<SettingDescription> Describe()
    {
        yield return new("SpeedTolerance", "车速容差", "km/h", SpeedTolerance, 0.5, 0, 50);
        yield return new("RpmTolerance", "转速容差", "r/min", RpmTolerance, 20, 0, 5000);
        yield return new("TemperatureTolerance", "温度容差", "°C", TemperatureTolerance, 2, 0, 30);
        yield return new("DurationTolerance", "积分时长容差", "s", DurationTolerance, 0.01, 0, 3600);
        yield return new("DriftThreshold", "漂移绝对差阈值", "dB", DriftThreshold, 1, 0, 30);
        yield return new("RecoveryThreshold", "恢复绝对差阈值", "dB", RecoveryThreshold, 1, 0, 30);
        yield return new("RepeatabilityThreshold", "重复性样本标准差阈值", "dB", RepeatabilityThreshold, 1, 0, 30);
        yield return new("MinimumEffectDisplay", "变化标记绝对值阈值", "dB", MinimumEffectDisplay, 0, 0, 30);
    }
}

public sealed record SettingDescription(
    string Key,
    string Label,
    string Unit,
    double Value,
    double Default,
    double Minimum,
    double Maximum);

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public long Revision { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Detail { get; set; } = "";
    public string InputHash { get; set; } = "";
}

public sealed class ReviewRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public long InputRevision { get; set; }
    public string Reviewer { get; set; } = "";
    public string Comment { get; set; } = "";
    public int ValidPairs { get; set; }
    public int BlockedPairs { get; set; }
    public List<string> BlockingReasons { get; set; } = [];
    public string SettingsJson { get; set; } = "";
    public string InputHash { get; set; } = "";
}
