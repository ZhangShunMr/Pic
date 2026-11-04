using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.Core.Analysis;

public sealed record PointDelta(string Name, double X, double Y, double Baseline, double Target, double Difference);

public sealed class Comparison
{
    public bool Comparable => Reasons.Count == 0;
    public string BaselineName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public List<string> Reasons { get; init; } = [];
    public Calculation? Baseline { get; init; }
    public Calculation? Target { get; init; }
    public List<PointDelta> Points { get; init; } = [];
    public string BaselineSource { get; init; } = "";
    public string TargetSource { get; init; } = "";
    public string Conditions { get; init; } = "";
    public double? CoverageDifference => Comparable && Baseline is not null && Target is not null
        ? Target.Coverage - Baseline.Coverage : null;
}

public static class ScenarioEngine
{
    public static Snapshot Capture(Project project, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new ArgumentException("方案名称必填，最多100字符");
        var copy = ProjectCodec.Clone(project);
        return new Snapshot
        {
            Name = name.Trim(),
            InputHash = ProjectCodec.InputHash(project),
            Settings = copy.Settings,
            Speakers = copy.Speakers,
            Listeners = copy.Listeners
        };
    }

    public static Project Restore(Snapshot snapshot)
    {
        return ProjectCodec.Clone(new Project
        {
            Name = snapshot.Name,
            Settings = snapshot.Settings,
            Speakers = snapshot.Speakers,
            Listeners = snapshot.Listeners
        });
    }

    public static Comparison Compare(Snapshot baseline, Snapshot target)
    {
        var a = Restore(baseline);
        var b = Restore(target);
        var reasons = Conditions(a, b);
        if (reasons.Count > 0)
            return new Comparison { BaselineName = baseline.Name, TargetName = target.Name, Reasons = reasons };
        var resultA = FieldEngine.Calculate(a);
        var resultB = FieldEngine.Calculate(b);
        var points = resultA.Listeners.Select(p =>
        {
            var q = resultB.Listeners.Single(x => x.ListenerId == p.ListenerId);
            return new PointDelta(p.Name, p.X, p.Y, p.Signal, q.Signal, q.Signal - p.Signal);
        }).ToList();
        return new Comparison
        {
            BaselineName = baseline.Name,
            TargetName = target.Name,
            Baseline = resultA,
            Target = resultB,
            BaselineSource = ProjectCodec.SourceSummary(a),
            TargetSource = ProjectCodec.SourceSummary(b),
            Conditions = ProjectCodec.Conditions(a.Settings),
            Points = points
        };
    }

    public static List<string> Conditions(Project a, Project b)
    {
        var reasons = new List<string>();
        var settingsA = ProjectCodec.Json(a.Settings);
        var settingsB = ProjectCodec.Json(b.Settings);
        if (settingsA != settingsB)
            reasons.Add("计算区域、步长、接收高度、车辆轮廓、背景、计权、频带或工程目标不同；请在相同设置下重存快照。");
        var pointsA = a.Listeners.OrderBy(x => x.Id).Select(x => new { x.Id, x.X, x.Y });
        var pointsB = b.Listeners.OrderBy(x => x.Id).Select(x => new { x.Id, x.X, x.Y });
        if (ProjectCodec.Json(pointsA) != ProjectCodec.Json(pointsB))
            reasons.Add("观察点标识或坐标不同；不能按相似名称猜测对应关系。");
        var originsA = a.Speakers.Where(x => x.Enabled).Select(x => x.Origin).Distinct().Order().ToArray();
        var originsB = b.Speakers.Where(x => x.Enabled).Select(x => x.Origin).Distinct().Order().ToArray();
        if (!originsA.SequenceEqual(originsB))
            reasons.Add("两方案的数据来源类别不同，禁止形成无标记差值。");
        if (!a.Speakers.Any(x => x.Enabled) || !b.Speakers.Any(x => x.Enabled))
            reasons.Add("至少一个方案没有启用的扬声器。");
        return reasons;
    }
}

public enum ScanParameter
{
    Heading,
    Gain,
    PositionX,
    PositionY
}

public sealed class ScanRequest
{
    public Guid SpeakerId { get; set; }
    public ScanParameter Parameter { get; set; }
    public double Start { get; set; } = -45;
    public double End { get; set; } = 45;
    public double Step { get; set; } = 15;
}

public sealed record ScanRow(double Value, double Coverage, double Minimum, double Maximum, double WorstMargin, int CoveredPoints);

public sealed class ScanResult
{
    public string InputHash { get; init; } = "";
    public Guid SpeakerId { get; init; }
    public string SpeakerName { get; init; } = "";
    public ScanParameter Parameter { get; init; }
    public string Metric { get; init; } = "";
    public string Source { get; init; } = "";
    public string Conditions { get; init; } = "";
    public List<ScanRow> Rows { get; init; } = [];
}

public static class ScanEngine
{
    public static ScanResult Run(Project original, ScanRequest request)
    {
        if (!double.IsFinite(request.Start) || !double.IsFinite(request.End) || !double.IsFinite(request.Step)
            || request.Step <= 0 || request.End < request.Start)
            throw new ArgumentException("扫描起止必须为有限数，终值不小于初值，步长必须为正");
        var count = (int)Math.Min(1000, Math.Floor((request.End - request.Start) / request.Step) + 1);
        if (count is < 1 or > 25)
            throw new ArgumentException("每次扫描最多25个取值，请增大步长");
        if (!Enum.IsDefined(request.Parameter))
            throw new ArgumentException("扫描参数无效");
        var project = ProjectCodec.Clone(original);
        var speaker = project.Speakers.SingleOrDefault(x => x.Id == request.SpeakerId)
            ?? throw new ArgumentException("请先选择需要扫描的扬声器");
        if (!speaker.Enabled)
            throw new ArgumentException("扫描对象必须启用");
        var rows = new List<ScanRow>();
        for (var i = 0; i < count; i++)
        {
            var value = request.Start + i * request.Step;
            Apply(speaker, request.Parameter, value);
            var result = FieldEngine.Calculate(project);
            rows.Add(new ScanRow(value, result.Coverage, result.Minimum, result.Maximum,
                result.Grid.Min(x => x.Margin), result.Listeners.Count(x => x.Covered)));
        }
        return new ScanResult
        {
            InputHash = ProjectCodec.InputHash(original),
            SpeakerId = speaker.Id,
            SpeakerName = speaker.Name,
            Parameter = request.Parameter,
            Metric = Acoustics.Metric(original.Settings),
            Source = ProjectCodec.SourceSummary(original),
            Conditions = ProjectCodec.Conditions(original.Settings),
            Rows = rows
        };
    }

    public static void Apply(Speaker speaker, ScanParameter parameter, double value)
    {
        switch (parameter)
        {
            case ScanParameter.Heading:
                speaker.Heading = value;
                break;
            case ScanParameter.Gain:
                speaker.Gain = value;
                break;
            case ScanParameter.PositionX:
                speaker.X = value;
                break;
            case ScanParameter.PositionY:
                speaker.Y = value;
                break;
            default:
                throw new ArgumentException("不支持的扫描参数");
        }
    }

    public static string Label(ScanParameter parameter) => parameter switch
    {
        ScanParameter.Heading => "朝向 °",
        ScanParameter.Gain => "增益 dB",
        ScanParameter.PositionX => "X坐标 m",
        ScanParameter.PositionY => "Y坐标 m",
        _ => "未知"
    };
}
