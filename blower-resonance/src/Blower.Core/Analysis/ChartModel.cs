using System.Globalization;
using System.Text;
using Blower.Core.Domain;

namespace Blower.Core.Analysis;

public sealed record PlotLine(string Id, string Name, string Color, double X1, double Y1, double X2, double Y2);
public sealed record PlotBand(string Id, string Name, double Y, double Height, double LabelY);
public sealed record PlotGear(string Id, string Name, double X, double Rpm, bool Near);
public sealed record PlotTick(double Position, string Label);

public sealed class ChartModel
{
    public const double Width = 880;
    public const double Height = 420;
    public const double Left = 68;
    public const double Right = 844;
    public const double Top = 30;
    public const double Bottom = 358;
    public List<PlotLine> Lines { get; } = [];
    public List<PlotBand> Bands { get; } = [];
    public List<PlotGear> Gears { get; } = [];
    public List<PlotTick> XTicks { get; } = [];
    public List<PlotTick> YTicks { get; } = [];
    public double MaximumHz { get; private set; }
    public double MinimumRpm { get; private set; }
    public double MaximumRpm { get; private set; }

    public double X(double rpm)
    {
        return Left + (rpm - MinimumRpm) / (MaximumRpm - MinimumRpm) * (Right - Left);
    }

    public double Y(double frequency)
    {
        return Bottom - frequency / MaximumHz * (Bottom - Top);
    }

    public static string N(double number) => number.ToString("0.###", CultureInfo.InvariantCulture);

    public static ChartModel Create(Project project, AnalysisResult result, string planId, double? ceiling = null)
    {
        var orders = project.Orders.Where(x => x.Enabled).ToList();
        var maximum = Math.Max(100,
            Math.Max(result.Bands.Select(x => x.UpperHz).DefaultIfEmpty(0).Max(),
            orders.Select(x => Engine.Frequency(project.Settings.MaxRpm, x.EffectiveOrder(project.Settings.Blades)))
                .DefaultIfEmpty(0).Max()));
        var chart = new ChartModel
        {
            MaximumHz = ceiling is > 0 ? ceiling.Value : Math.Ceiling(maximum / 100) * 100,
            MinimumRpm = project.Settings.MinRpm,
            MaximumRpm = project.Settings.MaxRpm
        };
        for (var i = 0; i <= 5; i++)
        {
            var rpm = chart.MinimumRpm + (chart.MaximumRpm - chart.MinimumRpm) * i / 5;
            var hz = chart.MaximumHz * i / 5;
            chart.XTicks.Add(new(chart.X(rpm), rpm.ToString("0")));
            chart.YTicks.Add(new(chart.Y(hz), hz.ToString("0")));
        }
        var colors = new[] { "#cf5d18", "#197493", "#b87916", "#2a866b", "#5866ad", "#aa4a7a" };
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var q = order.EffectiveOrder(project.Settings.Blades);
            var endRpm = Math.Min(chart.MaximumRpm, chart.MaximumHz * 60 / q);
            if (endRpm < chart.MinimumRpm)
            {
                continue;
            }
            chart.Lines.Add(new(order.Id, $"{order.Name} · {q:0.##}阶", colors[i % colors.Length],
                chart.X(chart.MinimumRpm), chart.Y(Engine.Frequency(chart.MinimumRpm, q)),
                chart.X(endRpm), chart.Y(Engine.Frequency(endRpm, q))));
        }
        foreach (var band in result.Bands.Where(x => x.LowerHz <= chart.MaximumHz))
        {
            var top = chart.Y(Math.Min(chart.MaximumHz, band.UpperHz));
            var bottom = chart.Y(band.LowerHz);
            chart.Bands.Add(new(band.ModeId, $"{band.Name} {band.CenterHz:0.#}Hz", top,
                Math.Max(2, bottom - top), Math.Clamp(chart.Y(band.CenterHz) - 5, Top + 10, Bottom - 5)));
        }
        var plan = project.Plans.FirstOrDefault(x => x.Id == planId);
        if (plan != null)
        {
            foreach (var gear in plan.Gears)
            {
                chart.Gears.Add(new(gear.Id, gear.Name, chart.X(gear.Rpm), gear.Rpm,
                    result.Contacts.Any(x => x.PlanId == planId && x.GearId == gear.Id && x.Near)));
            }
        }
        return chart;
    }

    public string ToSvg(string title, string source)
    {
        static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
        var b = new StringBuilder();
        b.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height + 60}\"><rect width=\"100%\" height=\"100%\" fill=\"#fffefa\"/>");
        b.Append("<g font-family=\"sans-serif\" font-size=\"11\" fill=\"#4c4c52\">");
        foreach (var tick in XTicks)
        {
            b.Append($"<path d=\"M{N(tick.Position)} {Top}V{Bottom}\" stroke=\"#deded9\"/><text x=\"{N(tick.Position)}\" y=\"380\" text-anchor=\"middle\">{E(tick.Label)}</text>");
        }
        foreach (var tick in YTicks)
        {
            b.Append($"<path d=\"M{Left} {N(tick.Position)}H{Right}\" stroke=\"#deded9\"/><text x=\"60\" y=\"{N(tick.Position + 4)}\" text-anchor=\"end\">{E(tick.Label)}</text>");
        }
        foreach (var band in Bands)
        {
            b.Append($"<rect x=\"{Left}\" y=\"{N(band.Y)}\" width=\"{Right - Left}\" height=\"{N(band.Height)}\" fill=\"#9b85bb\" opacity=\".25\"/><text x=\"{Right - 4}\" y=\"{N(band.LabelY)}\" text-anchor=\"end\">{E(band.Name)}</text>");
        }
        foreach (var line in Lines)
        {
            b.Append($"<path d=\"M{N(line.X1)} {N(line.Y1)}L{N(line.X2)} {N(line.Y2)}\" fill=\"none\" stroke=\"{line.Color}\" stroke-width=\"2\"/>");
        }
        foreach (var gear in Gears)
        {
            b.Append($"<path d=\"M{N(gear.X)} {Top}V{Bottom}\" stroke=\"{(gear.Near ? "#b44932" : "#267b67")}\" stroke-dasharray=\"5 5\"/><text x=\"{N(gear.X)}\" y=\"22\" text-anchor=\"middle\">{E(gear.Name)}</text>");
        }
        b.Append($"<text x=\"{Left}\" y=\"405\">转速 rpm →</text><text x=\"10\" y=\"16\">频率 Hz</text><text x=\"{Left}\" y=\"435\">{E(title)}</text><text x=\"{Left}\" y=\"455\">{E(source)} · {E(Software.Boundary)}</text></g></svg>");
        return b.ToString();
    }
}
