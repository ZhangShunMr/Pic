namespace MotorFrequency.Core.Domain;

public static class Software
{
    public const string Name = "汽车附件电机电磁激励频率设计软件";
    public const string Version = "V2.0";
    public const int FormatVersion = 1;
    public const string Extension = ".motorproj";
    public const string Boundary = "候选频率为运动学或限定假设下的参考，不代表实际电磁力、声压主峰或故障根因。";
}

public enum MotorTopology
{
    BrushlessPermanentMagnet,
    BrushedPermanentMagnet
}

public enum DataOrigin
{
    Manual,
    MeasuredDeclared,
    Simulated,
    Estimated
}

public sealed class Project
{
    public int FormatVersion { get; set; } = Software.FormatVersion;
    public string SoftwareVersion { get; set; } = Software.Version;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名项目";
    public string Owner { get; set; } = "";
    public string Purpose { get; set; } = "";
    public bool Demonstration { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public MotorConfiguration Motor { get; set; } = new();
    public List<CustomOrder> CustomOrders { get; set; } = [];
    public List<SensitiveBand> Bands { get; set; } = [];
    public List<Spectrum> Spectra { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];
    public List<AuditEvent> History { get; set; } = [];
    public ReviewSettings Review { get; set; } = new();
}

public sealed class MotorConfiguration
{
    public string Name { get; set; } = "当前电机";
    public MotorTopology Topology { get; set; }
    public int PolePairs { get; set; } = 2;
    public int Slots { get; set; } = 12;
    public int CommutatorSegments { get; set; } = 10;
    public double MinimumRpm { get; set; }
    public double MaximumRpm { get; set; } = 6000;
    public double StepRpm { get; set; } = 100;
    public double InspectRpm { get; set; } = 1800;
    public double MaximumHz { get; set; } = 1500;
    public bool IncludeSlotPass { get; set; } = true;
    public bool IncludeIdealCogging { get; set; }
    public bool SymmetricUnskewedAssumption { get; set; }
    public string AssumptionNotes { get; set; } = "";
    public List<string> HiddenCandidates { get; set; } = [];
}

public sealed class CustomOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "自定义候选";
    public double Order { get; set; } = 2;
    public string Basis { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class SensitiveBand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "敏感频段";
    public double LowerHz { get; set; } = 55;
    public double UpperHz { get; set; } = 65;
    public DataOrigin Origin { get; set; } = DataOrigin.Manual;
    public string Basis { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Spectrum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public double Rpm { get; set; }
    public string Unit { get; set; } = "dB";
    public string Reference { get; set; } = "";
    public string Condition { get; set; } = "";
    public DataOrigin Origin { get; set; } = DataOrigin.Manual;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string InputSha256 { get; set; } = "";
    public bool Visible { get; set; } = true;
    public List<SpectrumPoint> Points { get; set; } = [];
}

public sealed class SpectrumPoint
{
    public double FrequencyHz { get; set; }
    public double Amplitude { get; set; }
}

public sealed class Scenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public MotorConfiguration Motor { get; set; } = new();
    public List<CustomOrder> CustomOrders { get; set; } = [];
    public bool Demonstration { get; set; }
}

public sealed class AuditEvent
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class ReviewSettings
{
    public double MatchToleranceHz { get; set; } = 3;
    public int MaximumPeaks { get; set; } = 10;
    public string Notes { get; set; } = "";
}

public sealed record Candidate(
    string Id,
    string Name,
    double Order,
    string Formula,
    string Basis,
    string Category,
    string ObjectKey,
    bool Visible);

public sealed record FrequencyRow(
    string CandidateId,
    string Name,
    double Order,
    double Rpm,
    double FrequencyHz,
    string Formula,
    string Basis);

public sealed record BandCrossing(
    string CandidateId,
    string CandidateName,
    string BandName,
    Guid BandId,
    DataOrigin BandOrigin,
    double LowerRpm,
    double UpperRpm,
    double LowerHz,
    double UpperHz,
    string Basis);

public sealed record PeakMatch(
    string SpectrumName,
    double Rpm,
    double ObservedHz,
    double Amplitude,
    string Unit,
    string CandidateName,
    double? ExpectedHz,
    double? DifferenceHz,
    string Statement);

public sealed record ComparisonRow(
    string Name,
    double? CurrentOrder,
    double? SnapshotOrder,
    double? CurrentHz,
    double? SnapshotHz,
    double? DifferenceHz,
    string Statement);

public sealed record ValidationIssue(string Path, string Message);

public sealed record CsvImportResult(
    Spectrum? Spectrum,
    IReadOnlyList<ValidationIssue> Issues,
    int ParsedRows)
{
    public bool Success => Spectrum is not null && Issues.Count == 0;
}

public sealed record SpectrumMetadata(
    string Name,
    double Rpm,
    string Unit,
    string Reference,
    string Condition,
    DataOrigin Origin,
    string FileName);
