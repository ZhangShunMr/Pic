using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public sealed class AnalysisReport
{
    public Guid ProjectId { get; init; }
    public long Revision { get; init; }
    public string SoftwareVersion { get; init; } = Software.Version;
    public DateTimeOffset CalculatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Limitation { get; init; } = Software.Limitation;
    public List<PairResult> Pairs { get; init; } = [];
    public List<EffectSummary> Effects { get; init; } = [];
    public List<RepeatabilityGroup> Repeatability { get; init; } = [];
    public List<QualityIssue> Issues { get; init; } = [];
    public CoverageReport Coverage { get; init; } = new();
    public int ValidPairCount => Pairs.Count(pair => pair.Status == ResultStatus.Valid);
    public int BlockedPairCount => Pairs.Count(pair => pair.Status == ResultStatus.Blocked);
    public int WarningCount => Issues.Count(issue => issue.Severity == IssueSeverity.Warning);
}

public sealed class PairResult
{
    public Guid PairId { get; init; }
    public Guid BaselineId { get; init; }
    public Guid TargetId { get; init; }
    public string BaselineName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public PairPurpose Purpose { get; init; }
    public TrialKind TargetKind { get; init; }
    public SourceKind Source { get; init; }
    public List<Guid> LocationIds { get; init; } = [];
    public ResultStatus Status { get; init; }
    public List<string> BlockingReasons { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<BandDifference> Bands { get; init; } = [];
    public double? BaselineTotal { get; init; }
    public double? TargetTotal { get; init; }
    public double? TotalDifference { get; init; }
    public string TotalNote { get; init; } = "";
    public double ElapsedSeconds { get; init; }
    public ScreeningResult? Screening { get; init; }
    public string BaselineSource { get; init; } = "";
    public string TargetSource { get; init; } = "";
    public string Title => BaselineName + " → " + TargetName;
}

public sealed class BandDifference
{
    public double Frequency { get; init; }
    public double? Baseline { get; init; }
    public double? Target { get; init; }
    public double? Difference { get; init; }
    public ResultStatus Status { get; init; }
    public string Interpretation { get; init; } = "";
}

public sealed record ScreeningResult(
    double Threshold,
    double? MaximumAbsoluteDifference,
    double? WorstFrequency,
    ResultStatus Status);

public sealed class DescriptiveStatistics
{
    public int Count { get; init; }
    public double? ArithmeticMean { get; init; }
    public double? EnergyMean { get; init; }
    public double? StandardDeviation { get; init; }
    public double? Range { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}

public sealed class PairedStatistics
{
    public int Count { get; init; }
    public double? MeanDifference { get; init; }
    public double? StandardDeviation { get; init; }
    public double? GroupEnergyMeanDifference { get; init; }
    public double? Lower95 { get; init; }
    public double? Upper95 { get; init; }
    public string IntervalNote { get; init; } = "";
}

public sealed class EffectBandSummary
{
    public double Frequency { get; init; }
    public PairedStatistics Statistics { get; init; } = new();
}

public sealed class EffectSummary
{
    public string Key { get; init; } = "";
    public List<Guid> LocationIds { get; init; } = [];
    public SourceKind Source { get; init; }
    public bool IndependentPairs { get; init; }
    public List<Guid> PairIds { get; init; } = [];
    public List<string> BlockingReasons { get; init; } = [];
    public List<EffectBandSummary> Bands { get; init; } = [];
}

public sealed class RepeatabilityBand
{
    public double Frequency { get; init; }
    public int MissingCount { get; init; }
    public DescriptiveStatistics Statistics { get; init; } = new();
    public ResultStatus Status { get; init; }
}

public sealed class RepeatabilityGroup
{
    public string Name { get; init; } = "";
    public TrialKind Kind { get; init; }
    public SourceKind Source { get; init; }
    public List<Guid> LocationIds { get; init; } = [];
    public List<Guid> TrialIds { get; init; } = [];
    public List<string> BlockingReasons { get; init; } = [];
    public double Threshold { get; init; }
    public List<RepeatabilityBand> Bands { get; init; } = [];
}

public sealed record QualityIssue(
    IssueSeverity Severity,
    string Code,
    string EntityType,
    Guid EntityId,
    string Title,
    string Reason,
    string Repair);

public sealed class CoverageReport
{
    public List<ConditionCoverage> Conditions { get; init; } = [];
    public List<LocationCoverage> Locations { get; init; } = [];
    public List<FrequencyCoverage> Frequencies { get; init; } = [];
    public List<TimelineEntry> Timeline { get; init; } = [];
    public Dictionary<TrialKind, int> TrialTypes { get; init; } = [];
    public string Note { get; init; } = "覆盖只描述录入记录，不证明样本充分或测量可信。";
}

public sealed record ConditionCoverage(Guid Id, string Name, int TrialCount, List<string> Missing);
public sealed record LocationCoverage(Guid Id, string Name, int SingleCount, int CombinationCount, int ValidPairCount);
public sealed record FrequencyCoverage(double Frequency, int Valid, int Missing, int Undeclared);
public sealed record TimelineEntry(Guid Id, string Name, TrialKind Kind, SourceKind Source, DateTimeOffset Time, int ValidBands, int TargetPairs);
