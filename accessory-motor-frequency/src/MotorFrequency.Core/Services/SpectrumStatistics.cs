using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public sealed record SpectrumSummary(
    double MinimumHz,
    double MaximumHz,
    double MinimumSpacingHz,
    double MaximumSpacingHz,
    double MedianSpacingHz,
    bool UniformSpacing,
    double MaximumAmplitude,
    double MaximumAtHz,
    int PointsInsideView,
    int TotalPoints,
    string Statement);

public static class SpectrumStatistics
{
    public static SpectrumSummary Inspect(Spectrum spectrum, double maximumViewHz)
    {
        var issues = ProjectValidator.ValidateSpectrum(spectrum);
        if (issues.Count > 0)
        {
            throw new InvalidDataException("无法汇总无效频谱：" + issues[0].Message);
        }
        var points = spectrum.Points;
        var spacings = new List<double>();
        for (var i = 1; i < points.Count; i++)
        {
            spacings.Add(points[i].FrequencyHz - points[i - 1].FrequencyHz);
        }
        spacings.Sort();
        var middle = spacings.Count / 2;
        var median = spacings.Count % 2 == 0
            ? (spacings[middle - 1] + spacings[middle]) / 2
            : spacings[middle];
        var minimum = spacings[0];
        var maximum = spacings[^1];
        var uniform = maximum - minimum <= Math.Max(1e-9, maximum * 1e-6);
        var maximumPoint = points.MaxBy(x => x.Amplitude)!;
        var inside = points.Count(x => x.FrequencyHz <= maximumViewHz);
        var statement = uniform
            ? "频率间距近似一致；仅汇总输入网格，不推断时域采样率或FFT分辨率。"
            : "频率间距不一致；保留原始网格，不做重采样，峰值邻近结果需结合局部间距阅读。";
        if (maximumPoint == points[0] || maximumPoint == points[^1])
        {
            statement += " 最大输入幅值在端点，不会作为内部谱峰参与邻近核对。";
        }
        if (inside < points.Count)
        {
            statement += " 部分原始频点超出当前图表上限。";
        }
        return new SpectrumSummary(
            points[0].FrequencyHz,
            points[^1].FrequencyHz,
            minimum,
            maximum,
            median,
            uniform,
            maximumPoint.Amplitude,
            maximumPoint.FrequencyHz,
            inside,
            points.Count,
            statement);
    }
}
