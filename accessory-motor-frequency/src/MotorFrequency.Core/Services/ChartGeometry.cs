using System.Globalization;
using System.Text;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public sealed class ChartGeometry
{
    public const double Width = 900;
    public const double Height = 370;
    public const double Left = 68;
    public const double Right = 860;
    public const double Top = 24;
    public const double Bottom = 316;
    private static readonly string[] palette = ["#1d6860", "#b65f32", "#566da1", "#9a7c27", "#85608b", "#4b7c8b", "#6d7743"];
    public MotorConfiguration Motor { get; }

    public ChartGeometry(MotorConfiguration motor)
    {
        Motor = motor;
    }

    public double X(double rpm)
    {
        return Left + (rpm - Motor.MinimumRpm) / (Motor.MaximumRpm - Motor.MinimumRpm) * (Right - Left);
    }

    public double Y(double hz)
    {
        return Bottom - hz / Motor.MaximumHz * (Bottom - Top);
    }

    public static string N(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string CandidateColor(string id)
    {
        uint hash = 2166136261;
        foreach (var character in id)
        {
            hash = unchecked((hash ^ character) * 16777619);
        }
        return palette[hash % palette.Length];
    }

    public string CandidateLine(Candidate candidate)
    {
        var lower = Motor.MinimumRpm;
        var upper = Math.Min(Motor.MaximumRpm, 60 * Motor.MaximumHz / candidate.Order);
        if (upper < lower)
        {
            return "";
        }
        return $"M {N(X(lower))} {N(Y(FrequencyEngine.At(candidate.Order, lower)))} L {N(X(upper))} {N(Y(FrequencyEngine.At(candidate.Order, upper)))}";
    }

    public IEnumerable<(double Value, double Position)> SpeedTicks()
    {
        for (var i = 0; i <= 6; i++)
        {
            var value = Motor.MinimumRpm + (Motor.MaximumRpm - Motor.MinimumRpm) * i / 6;
            yield return (value, X(value));
        }
    }

    public IEnumerable<(double Value, double Position)> FrequencyTicks()
    {
        for (var i = 0; i <= 5; i++)
        {
            var value = Motor.MaximumHz * i / 5;
            yield return (value, Y(value));
        }
    }

    public static IReadOnlyList<SpectrumPoint> DisplayPoints(Spectrum spectrum, int maximum = 1000)
    {
        if (spectrum.Points.Count <= maximum)
        {
            return spectrum.Points;
        }
        var output = new List<SpectrumPoint>();
        var bucketCount = maximum / 2;
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = bucket * spectrum.Points.Count / bucketCount;
            var end = (bucket + 1) * spectrum.Points.Count / bucketCount;
            var segment = spectrum.Points.GetRange(start, end - start);
            var low = segment.MinBy(x => x.Amplitude)!;
            var high = segment.MaxBy(x => x.Amplitude)!;
            if (low.FrequencyHz <= high.FrequencyHz)
            {
                output.Add(low);
                if (low != high) output.Add(high);
            }
            else
            {
                output.Add(high);
                output.Add(low);
            }
        }
        return output;
    }

    public string Svg(Project project)
    {
        var text = new StringBuilder();
        var allCandidates = FrequencyEngine.Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible).ToList();
        var height = 450 + (int)Math.Ceiling(allCandidates.Count / 2.0) * 22;
        text.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 " + height + "\" role=\"img\">");
        text.AppendLine("<rect width=\"900\" height=\"" + height + "\" fill=\"#faf9f4\"/>");
        foreach (var tick in FrequencyTicks())
        {
            text.AppendLine($"<path d=\"M 68 {N(tick.Position)} H 860\" stroke=\"#dddcd3\"/>");
            text.AppendLine($"<text x=\"58\" y=\"{N(tick.Position + 4)}\" text-anchor=\"end\" font-size=\"12\">{Labels.Number(tick.Value)}</text>");
        }
        foreach (var tick in SpeedTicks())
        {
            text.AppendLine($"<path d=\"M {N(tick.Position)} 24 V 316\" stroke=\"#e7e5dc\"/>");
            text.AppendLine($"<text x=\"{N(tick.Position)}\" y=\"337\" text-anchor=\"middle\" font-size=\"12\">{Labels.Number(tick.Value)}</text>");
        }
        foreach (var band in project.Bands.Where(x => x.Enabled && x.LowerHz <= Motor.MaximumHz))
        {
            var upper = Math.Min(band.UpperHz, Motor.MaximumHz);
            text.AppendLine($"<rect x=\"68\" y=\"{N(Y(upper))}\" width=\"792\" height=\"{N(Math.Max(1, Y(band.LowerHz) - Y(upper)))}\" fill=\"#b19136\" opacity=\"0.16\"/>");
        }
        var candidates = allCandidates;
        for (var i = 0; i < candidates.Count; i++)
        {
            text.AppendLine($"<path d=\"{CandidateLine(candidates[i])}\" stroke=\"{CandidateColor(candidates[i].Id)}\" fill=\"none\" stroke-width=\"2\"/>");
        }
        text.AppendLine($"<path d=\"M {N(X(Motor.InspectRpm))} 24 V 316\" stroke=\"#333\" stroke-dasharray=\"5 5\"/>");
        text.AppendLine("<text x=\"10\" y=\"18\" font-size=\"12\">Hz</text><text x=\"790\" y=\"357\" font-size=\"12\">r/min</text>");
        text.AppendLine("<text x=\"68\" y=\"380\" font-size=\"14\">" + System.Net.WebUtility.HtmlEncode(project.Name.Length > 44 ? project.Name[..44] + "…" : project.Name) + " · " + Software.Version + "</text>");
        text.AppendLine("<text x=\"68\" y=\"403\" font-size=\"12\">候选频率参考；" + (project.Demonstration ? "模拟演示" : "人工参数") + "；非声压预测</text>");
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var x = 68 + index % 2 * 395;
            var y = 438 + index / 2 * 22;
            var label = candidate.Name.Length > 25 ? candidate.Name[..25] + "…" : candidate.Name;
            text.AppendLine($"<path d=\"M {x} {y - 4} h 20\" stroke=\"{CandidateColor(candidate.Id)}\" stroke-width=\"3\"/>");
            text.AppendLine($"<text x=\"{x + 28}\" y=\"{y}\" font-size=\"12\">{System.Net.WebUtility.HtmlEncode(label)} · {N(candidate.Order)}阶</text>");
        }
        text.AppendLine("</svg>");
        return text.ToString();
    }
}
