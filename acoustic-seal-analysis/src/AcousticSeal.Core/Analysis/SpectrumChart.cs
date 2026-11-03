using System.Globalization;
using System.Text;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public static class SpectrumChart
{
    public static string Build(PairResult pair)
    {
        if (pair.Status == ResultStatus.Blocked)
        {
            return "<p>配对条件已阻断，不绘制可比频谱图。原始声级仍在数值表中保留。</p>";
        }

        var bands = pair.Bands.Where(band => band.Baseline.HasValue || band.Target.HasValue).ToList();
        if (bands.Count == 0)
        {
            return "<p>没有有效声级，无法绘制频谱。</p>";
        }

        if (bands.Count > 30)
        {
            return "<p>超过30个频带，请先缩窄界面频带扫描范围，或使用完整数值表。</p>";
        }

        var levels = bands.SelectMany(band => new[] { band.Baseline, band.Target }).Where(value => value.HasValue)
            .Select(value => value!.Value).ToList();
        var low = Math.Floor(levels.Min() / 5) * 5 - 5;
        var high = Math.Ceiling(levels.Max() / 5) * 5 + 5;
        var minFrequency = bands.Min(band => band.Frequency);
        var maxFrequency = bands.Max(band => band.Frequency);
        var minimumLog = Math.Log10(minFrequency);
        var logSpan = Math.Log10(maxFrequency) - minimumLog;
        double X(double frequency)
        {
            return logSpan == 0 ? 430 : 65 + (Math.Log10(frequency) - minimumLog) / logSpan * 710;
        }

        double Y(double level)
        {
            return 225 - (level - low) / (high - low) * 175;
        }

        var builder = new StringBuilder("<svg viewBox=\"0 0 820 300\" role=\"img\" aria-label=\"基线与目标原始频带声级图，横轴对数频率Hz，纵轴声级dB\">");
        builder.Append("<text x=\"8\" y=\"18\" font-size=\"11\">声级 dB re 20 µPa · 截断纵轴</text>");
        for (var index = 0; index < 5; index++)
        {
            var value = low + index * (high - low) / 4;
            var y = Y(value);
            builder.Append("<line x1=\"60\" x2=\"790\" y1=\"").Append(C(y)).Append("\" y2=\"").Append(C(y)).Append("\" stroke=\"#dce6e8\"/>");
            builder.Append("<text x=\"8\" y=\"").Append(C(y + 4)).Append("\" font-size=\"10\">").Append(C(value)).Append("</text>");
        }

        foreach (var band in bands)
        {
            builder.Append("<text x=\"").Append(C(X(band.Frequency))).Append("\" y=\"248\" text-anchor=\"middle\" font-size=\"10\">").Append(C(band.Frequency)).Append("</text>");
        }

        DrawSeries(builder, bands, band => band.Baseline, "#617b8a", "基线", X, Y);
        DrawSeries(builder, bands, band => band.Target, "#138d82", "目标", X, Y);
        builder.Append("<text x=\"140\" y=\"278\" fill=\"#617b8a\" font-size=\"12\">● 基线</text>");
        builder.Append("<text x=\"240\" y=\"278\" fill=\"#138d82\" font-size=\"12\">● 目标</text>");
        builder.Append("<text x=\"440\" y=\"278\" font-size=\"11\">缺失处断线；连线仅引导阅读，不插值补数</text>");
        builder.Append("<text x=\"755\" y=\"295\" font-size=\"11\">Hz（对数）</text></svg>");
        return builder.ToString();
    }

    private static void DrawSeries(StringBuilder output, List<BandDifference> bands, Func<BandDifference, double?> value,
        string color, string label, Func<double, double> x, Func<double, double> y)
    {
        var segment = new List<string>();
        void Flush()
        {
            if (segment.Count > 1)
            {
                output.Append("<polyline fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"2\" points=\"")
                    .Append(string.Join(" ", segment)).Append("\"/>");
            }

            segment.Clear();
        }

        foreach (var band in bands)
        {
            var level = value(band);
            if (!level.HasValue)
            {
                Flush();
                continue;
            }

            var point = C(x(band.Frequency)) + "," + C(y(level.Value));
            segment.Add(point);
            output.Append("<circle cx=\"").Append(C(x(band.Frequency))).Append("\" cy=\"").Append(C(y(level.Value)))
                .Append("\" r=\"4\" fill=\"").Append(color).Append("\"><title>").Append(label).Append(" ")
                .Append(C(band.Frequency)).Append("Hz ").Append(C(level.Value)).Append("dB</title></circle>");
        }

        Flush();
    }

    private static string C(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
