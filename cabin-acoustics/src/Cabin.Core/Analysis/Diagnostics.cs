using Cabin.Core.Domain;
using Cabin.Core.IO;

namespace Cabin.Core.Analysis;

public sealed record DensityBand(double Lower, double Upper, int Count, int Axial, int Tangential, int Oblique);
public sealed record CouplingDiagnostic(string ModeKey, double Frequency, double SourceMagnitude,
    double SeatShape, string Observation);
public sealed record SensitivityRow(string Seat, double OriginalRms, double ExtendedRms, double RatioDb,
    double OriginalPeak, double ExtendedPeak);
public sealed class TruncationStudy
{
    public double OriginalCutoff { get; init; }
    public double ExtendedCutoff { get; init; }
    public int OriginalModes { get; init; }
    public int ExtendedModes { get; init; }
    public List<SensitivityRow> Rows { get; init; } = [];
}

public static class Diagnostics
{
    public static List<DensityBand> Density(AnalysisResult result, double width = 25)
    {
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentException("频段宽度应大于零");
        var maximum = result.Modes.Select(x => x.Frequency).DefaultIfEmpty(0).Max();
        if (maximum / width > 10000)
            throw new ArgumentException("模态密度频段不得超过10000个");
        var rows = new List<DensityBand>();
        for (var lower = 0.0; lower <= maximum; lower += width)
        {
            var upper = lower + width;
            var modes = result.Modes.Where(x => x.Frequency >= lower && x.Frequency < upper).ToList();
            rows.Add(new(lower, upper, modes.Count, modes.Count(x => x.Dimension == 1),
                modes.Count(x => x.Dimension == 2), modes.Count(x => x.Dimension == 3)));
        }
        return rows;
    }

    public static List<CouplingDiagnostic> Coupling(Project project, AnalysisResult result, EarPoint seat)
    {
        var rows = new List<CouplingDiagnostic>();
        foreach (var mode in result.Modes.Where(x => x.Frequency <= project.Model.MaximumFrequency))
        {
            var source = Modes.SourceCoupling(mode, project.Model, project.Sources).Magnitude;
            var shape = Modes.Shape(mode, project.Model, seat.Point);
            var maximumAmplitude = project.Sources.Where(x => x.Enabled).Sum(x => x.Amplitude);
            var weakSource = source < Math.Max(1e-12, maximumAmplitude * 0.01);
            var weakSeat = Math.Abs(shape) < 0.01;
            var observation = weakSource && weakSeat ? "源与耳点均弱耦合" :
                weakSource ? "源弱耦合或多源抵消" : weakSeat ? "耳点接近此模态节点" : "存在耦合";
            rows.Add(new(mode.Key, mode.Frequency, source, shape, observation));
        }
        return rows;
    }

    public static TruncationStudy Extend(Project project, AnalysisResult original)
    {
        var extended = ProjectCodec.Clone(project);
        extended.Baselines.Clear();
        var cutoff = Math.Min(1000, project.Model.Cutoff + 100);
        if (cutoff <= project.Model.Cutoff)
            throw new ArgumentException("当前截断已达1000Hz上限，不能继续增加");
        extended.Model.Cutoff = cutoff;
        var result = Engine.Calculate(extended);
        var rows = new List<SensitivityRow>();
        foreach (var curve in original.Curves)
        {
            var other = result.Curves.First(x => x.Id == curve.Id);
            var ratio = curve.Rms > 1e-12 ? Engine.Decibels(other.Rms, curve.Rms) : double.NaN;
            rows.Add(new(curve.Name, curve.Rms, other.Rms, ratio, curve.Peak.Magnitude, other.Peak.Magnitude));
        }
        return new()
        {
            OriginalCutoff = project.Model.Cutoff,
            ExtendedCutoff = cutoff,
            OriginalModes = original.Modes.Count,
            ExtendedModes = result.Modes.Count,
            Rows = rows
        };
    }

    public static List<string> Advice(Project project, AnalysisResult result)
    {
        var advice = new List<string>();
        if (result.Modes.Count == 0)
            advice.Add("截断内没有非零模态，考虑提高截断频率。");
        if (result.Reference < 1e-12)
            advice.Add("总响应近零，检查源相位抵消、零幅值与模态节点。");
        var modes = result.Modes.Where(x => x.Frequency >= project.Model.MinimumFrequency &&
            x.Frequency <= project.Model.MaximumFrequency).ToList();
        if (modes.Any(x => project.Model.FrequencyStep > project.Model.Damping * x.Frequency))
            advice.Add("部分模态的频率步长大于ζfn，可能漏掉窄峰。请细化步长，使用滑杆观察连续频率响应。");
        if (project.Model.Cutoff < project.Model.MaximumFrequency * 1.5)
            advice.Add("截断频率距分析终点较近，建议增加截断并检查RMS变化。");
        if (modes.Any(x => x.Degeneracy > 1))
            advice.Add("频带内存在退化模态。单个退化振型的基底不唯一，应关注其叠加响应。");
        if (project.Sources.Count(x => x.Enabled) > 1)
            advice.Add("多源使用同一相位参考，幅值叠加必须保留复数相位。");
        advice.Add("以上均为数值检查提示；没有提示不代表实车模型已校准。");
        return advice;
    }
}
