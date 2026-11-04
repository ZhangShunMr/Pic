using Cabin.Core.Domain;
using Cabin.Core.IO;

namespace Cabin.Core.Analysis;

public sealed record SeatDifference(string Name, double BaselinePeak, double CurrentPeak,
    double PeakRatioDb, double BaselineFrequency, double CurrentFrequency, double RmsRatioDb);
public sealed record ModeDifference(string Key, double BaselineFrequency, double CurrentFrequency, double Percent);
public sealed class ComparisonResult
{
    public List<string> Warnings { get; init; } = [];
    public List<SeatDifference> Seats { get; init; } = [];
    public List<ModeDifference> Modes { get; init; } = [];
    public double Reference { get; init; }
    public AnalysisResult BaselineResult { get; init; } = new();
}

public static class Comparison
{
    public static ComparisonResult Compare(Project current, AnalysisResult currentResult, Snapshot baseline)
    {
        var oldProject = Demo.FromSnapshot(baseline);
        var oldResult = Engine.Calculate(oldProject);
        var warnings = new List<string>();
        if (current.Model.MinimumFrequency != baseline.Model.MinimumFrequency ||
            current.Model.MaximumFrequency != baseline.Model.MaximumFrequency ||
            current.Model.FrequencyStep != baseline.Model.FrequencyStep)
            throw new ArgumentException("分析频带或步长不同，不能比较。请恢复基准频带与步长后重算。");
        if (current.Model.Cutoff != baseline.Model.Cutoff)
            throw new ArgumentException("模态截断不同，不能比较。请使用相同截断频率。");
        if (current.DataSource != baseline.DataSource || current.IsDemo != baseline.IsDemo)
            throw new ArgumentException("来源或模拟标记不同，不能比较。");
        if (current.Model.Damping != baseline.Model.Damping)
            warnings.Add("阻尼已变化；差值同时包含阻尼影响。");
        if (current.Model.SoundSpeed != baseline.Model.SoundSpeed)
            warnings.Add("声速已变化；差值同时包含介质条件影响。");
        if (current.Model.Length != baseline.Model.Length || current.Model.Width != baseline.Model.Width || current.Model.Height != baseline.Model.Height)
            warnings.Add("尺寸变化；模态按阶次匹配，耳点使用绝对米制坐标。");
        var differences = new List<SeatDifference>();
        foreach (var curve in currentResult.Curves)
        {
            var old = oldResult.Curves.FirstOrDefault(x => x.Id == curve.Id);
            if (old is null)
            {
                warnings.Add(curve.Name + "没有相同标识的基准耳点，跳过此耳点。");
                continue;
            }
            var oldSeat = baseline.Seats.First(x => x.Id == curve.Id);
            var seat = current.Seats.First(x => x.Id == curve.Id);
            if (oldSeat.Point.X != seat.Point.X || oldSeat.Point.Y != seat.Point.Y || oldSeat.Point.Z != seat.Point.Z)
                warnings.Add(curve.Name + "位置变化；差值包含测点变化。");
            if (old.Peak.Magnitude < 1e-12 || old.Rms < 1e-12)
            {
                warnings.Add(curve.Name + "基准近零，不能计算稳定比值。");
                continue;
            }
            differences.Add(new(curve.Name, old.Peak.Magnitude, curve.Peak.Magnitude,
                Engine.Decibels(curve.Peak.Magnitude, old.Peak.Magnitude), old.Peak.Frequency,
                curve.Peak.Frequency, Engine.Decibels(curve.Rms, old.Rms)));
        }
        if (differences.Count == 0)
            throw new ArgumentException("没有可匹配的非零基准耳点");
        var modes = new List<ModeDifference>();
        foreach (var mode in currentResult.Modes)
        {
            var old = oldResult.Modes.FirstOrDefault(x => x.Key == mode.Key);
            if (old is not null)
                modes.Add(new(mode.Key, old.Frequency, mode.Frequency, (mode.Frequency / old.Frequency - 1) * 100));
        }
        return new()
        {
            Warnings = warnings,
            Seats = differences,
            Modes = modes,
            Reference = Math.Max(oldResult.Reference, currentResult.Reference),
            BaselineResult = oldResult
        };
    }
}

public sealed record ScanSettings(string Parameter, double Start, double End, int Count, Guid SeatId);
public sealed record ScanRow(double Parameter, double PeakMagnitude, double PeakFrequency, double Rms, double FirstMode, string Error);
public sealed class ScanResult
{
    public ScanSettings Settings { get; init; } = new("length", 3, 4, 5, Guid.Empty);
    public List<ScanRow> Rows { get; init; } = [];
    public string Unit => Settings.Parameter is "damping" ? "无量纲" : "m";
}

public static class ParameterScan
{
    public static ScanResult Run(Project project, ScanSettings settings)
    {
        if (settings.Count is < 2 or > 21)
            throw new ArgumentException("扫描点数应为2至21");
        if (!double.IsFinite(settings.Start) || !double.IsFinite(settings.End) || settings.Start >= settings.End)
            throw new ArgumentException("扫描终值应大于初值，且均为有限数值");
        if (!project.Seats.Any(x => x.Id == settings.SeatId))
            throw new ArgumentException("请选择扫描耳点");
        if (settings.Parameter is not ("length" or "width" or "height" or "sourceX" or "sourceY" or "sourceZ" or "damping"))
            throw new ArgumentException("不支持的扫描参数");
        var rows = new List<ScanRow>();
        for (var i = 0; i < settings.Count; i++)
        {
            var value = settings.Start + (settings.End - settings.Start) * i / (settings.Count - 1);
            var candidate = ProjectCodec.Clone(project);
            candidate.Baselines.Clear();
            Apply(candidate, settings.Parameter, value);
            try
            {
                var result = Engine.Calculate(candidate);
                var curve = result.Curves.First(x => x.Id == settings.SeatId);
                rows.Add(new(value, curve.Peak.Magnitude, curve.Peak.Frequency, curve.Rms,
                    result.Modes.FirstOrDefault()?.Frequency ?? 0, ""));
            }
            catch (ArgumentException error)
            {
                rows.Add(new(value, 0, 0, 0, 0, error.Message));
            }
        }
        return new() { Settings = settings, Rows = rows };
    }

    public static void Apply(Project project, string parameter, double value)
    {
        switch (parameter)
        {
            case "length": project.Model.Length = value; break;
            case "width": project.Model.Width = value; break;
            case "height": project.Model.Height = value; break;
            case "damping": project.Model.Damping = value; break;
            default:
                var source = project.Sources.FirstOrDefault() ?? throw new ArgumentException("没有首个声源可扫描");
                switch (parameter)
                {
                    case "sourceX": source.Point.X = value; break;
                    case "sourceY": source.Point.Y = value; break;
                    case "sourceZ": source.Point.Z = value; break;
                    default: throw new ArgumentException("未知参数");
                }
                break;
        }
    }
}
