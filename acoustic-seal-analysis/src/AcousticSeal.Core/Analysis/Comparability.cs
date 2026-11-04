using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public sealed class Comparability
{
    public List<string> Inspect(MeasurementCondition left, MeasurementCondition right, AnalysisSettings settings)
    {
        var reasons = new List<string>();
        var rightFields = right.TextFields().ToDictionary(field => field.Key);
        foreach (var field in left.TextFields())
        {
            var other = rightFields[field.Key];
            CompareText(field.Label, field.Value, other.Value, reasons);
        }

        CompareNumber("车速", "km/h", left.Speed, right.Speed, settings.SpeedTolerance, reasons);
        CompareNumber("转速", "r/min", left.Rpm, right.Rpm, settings.RpmTolerance, reasons);
        CompareNumber("温度", "°C", left.Temperature, right.Temperature, settings.TemperatureTolerance, reasons);
        CompareNumber("积分时长", "s", left.Duration, right.Duration, settings.DurationTolerance, reasons);
        if (left.NonOverlappingBands != right.NonOverlappingBands)
        {
            reasons.Add("互不重叠频带声明不同");
        }

        if (settings.RequireSampleRateMatch)
        {
            CompareNumber("采样率", "Hz", left.SampleRate, right.SampleRate, 0, reasons);
        }

        if (settings.RequireSynchronizationMatch)
        {
            CompareText("时钟与同步定义", left.Synchronization, right.Synchronization, reasons);
        }

        return reasons;
    }

    public List<string> MissingFields(MeasurementCondition condition, AnalysisSettings settings)
    {
        return Inspect(condition, condition, settings);
    }

    private static void CompareText(string label, string? left, string? right, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            reasons.Add(label + "缺失，无法确认可比性");
        }
        else if (!string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal))
        {
            reasons.Add(label + "不同：" + left + " / " + right);
        }
    }

    private static void CompareNumber(string label, string unit, double? left, double? right, double tolerance, List<string> reasons)
    {
        if (!left.HasValue || !right.HasValue)
        {
            reasons.Add(label + "缺失，无法确认可比性");
            return;
        }

        var difference = Math.Abs(left.Value - right.Value);
        if (difference > tolerance + 1e-10)
        {
            reasons.Add($"{label}绝对差{difference:G6} {unit}超过容差{tolerance:G6}");
        }
    }
}
