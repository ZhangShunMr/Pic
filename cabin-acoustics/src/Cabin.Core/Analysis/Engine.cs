using System.Numerics;
using Cabin.Core.Domain;

namespace Cabin.Core.Analysis;

public sealed record ResponsePoint(double Frequency, double Real, double Imaginary)
{
    public double Magnitude => Math.Sqrt(Real * Real + Imaginary * Imaginary);
    public double Phase => Math.Atan2(Imaginary, Real) * 180 / Math.PI;
    public double RelativeDb(double reference) => Engine.Decibels(Magnitude, reference);
}

public sealed record SeatCurve(Guid Id, string Name, List<ResponsePoint> Points)
{
    public ResponsePoint Peak => Points.OrderByDescending(x => x.Magnitude).First();
    public double Rms => Math.Sqrt(Points.Average(x => x.Magnitude * x.Magnitude));
}

public sealed class AnalysisResult
{
    public List<Mode> Modes { get; init; } = [];
    public List<SeatCurve> Curves { get; init; } = [];
    public List<double> Frequencies { get; init; } = [];
    public double Reference { get; init; }
    public DateTimeOffset Computed { get; init; } = DateTimeOffset.UtcNow;
    public int InBandModes(double minimum, double maximum) => Modes.Count(x => x.Frequency >= minimum && x.Frequency <= maximum);
}

public sealed record Contribution(string Key, double Frequency, double Magnitude, double Phase, double SourceMagnitude, double SeatShape);
public sealed record FieldCell(double U, double V, double Value);
public sealed record FieldResult(List<FieldCell> Cells, double Maximum, string Plane, double Slice, string Quantity);

public static class Engine
{
    public static List<double> FrequencyGrid(Cavity model)
    {
        var frequencies = new List<double>();
        var count = (int)Math.Floor((model.MaximumFrequency - model.MinimumFrequency) / model.FrequencyStep + 1e-8);
        for (var i = 0; i <= count; i++)
            frequencies.Add(model.MinimumFrequency + i * model.FrequencyStep);
        if (frequencies.Count == 0 || model.MaximumFrequency - frequencies[^1] > 1e-8)
            frequencies.Add(model.MaximumFrequency);
        return frequencies;
    }

    public static AnalysisResult Calculate(Project project, CancellationToken cancellationToken = default)
    {
        var issues = Validation.Check(project);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("；", issues.Select(x => x.Path + "：" + x.Message)));
        var modes = Modes.Enumerate(project.Model);
        var frequencies = FrequencyGrid(project.Model);
        if ((long)modes.Count * frequencies.Count * project.Seats.Count > 30_000_000)
            throw new ArgumentException("计算量超过3000万模态项，请减少截断、采样或耳点数量");
        var couplings = modes.Select(x => Modes.SourceCoupling(x, project.Model, project.Sources)).ToList();
        var curves = new List<SeatCurve>();
        foreach (var seat in project.Seats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = new List<ResponsePoint>();
            foreach (var frequency in frequencies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = At(project.Model, modes, couplings, seat.Point, frequency);
                points.Add(new(frequency, response.Real, response.Imaginary));
            }
            curves.Add(new(seat.Id, seat.Name, points));
        }
        var reference = curves.SelectMany(x => x.Points).Select(x => x.Magnitude).DefaultIfEmpty(0).Max();
        return new()
        {
            Modes = modes,
            Frequencies = frequencies,
            Curves = curves,
            Reference = reference
        };
    }

    public static Complex At(Cavity model, IReadOnlyList<Mode> modes, IReadOnlyList<Complex> couplings, Position point, double frequency)
    {
        var sum = Complex.Zero;
        for (var i = 0; i < modes.Count; i++)
            sum += Modes.Term(modes[i], model, couplings[i], point, frequency);
        return sum;
    }

    public static Complex At(Project project, AnalysisResult result, Position point, double frequency)
    {
        var couplings = result.Modes.Select(x => Modes.SourceCoupling(x, project.Model, project.Sources)).ToList();
        return At(project.Model, result.Modes, couplings, point, frequency);
    }

    public static List<Contribution> Contributions(Project project, AnalysisResult result, EarPoint seat, double frequency)
    {
        return result.Modes.Select(mode =>
        {
            var source = Modes.SourceCoupling(mode, project.Model, project.Sources);
            var value = Modes.Term(mode, project.Model, source, seat.Point, frequency);
            return new Contribution(mode.Key, mode.Frequency, value.Magnitude,
                value.Phase * 180 / Math.PI, source.Magnitude, Modes.Shape(mode, project.Model, seat.Point));
        }).OrderByDescending(x => x.Magnitude).ToList();
    }

    public static FieldResult Field(Project project, AnalysisResult result, string plane, double fraction,
        bool shape, Mode? mode, double frequency, int columns = 48, int rows = 24)
    {
        if (plane is not ("xy" or "xz" or "yz"))
            throw new ArgumentException("剖面只能为xy、xz、yz");
        if (fraction < 0 || fraction > 1 || !double.IsFinite(fraction))
            throw new ArgumentException("剖面位置必须在0至1之间");
        if (columns is < 2 or > 128 || rows is < 2 or > 96)
            throw new ArgumentException("剖面网格列数2—128、行数2—96");
        if (!double.IsFinite(frequency) || frequency < project.Model.MinimumFrequency || frequency > project.Model.MaximumFrequency)
            throw new ArgumentException("研究频率必须位于分析频带内");
        if (shape && mode is null)
            throw new ArgumentException("振型显示需要选定模态");
        var cells = new List<FieldCell>();
        var couplings = result.Modes.Select(x => Modes.SourceCoupling(x, project.Model, project.Sources)).ToList();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var u = (column + 0.5) / columns;
                var v = (row + 0.5) / rows;
                var point = PlanePoint(project.Model, plane, u, v, fraction);
                var value = shape && mode is not null ? Modes.Shape(mode, project.Model, point) :
                    At(project.Model, result.Modes, couplings, point, frequency).Magnitude;
                cells.Add(new(u, v, value));
            }
        }
        var maximum = shape ? 1 : cells.Select(x => x.Value).DefaultIfEmpty(0).Max();
        return new(cells, maximum, plane, fraction, shape ? "有符号归一化振型" : "剖面局部归一化响应幅值");
    }

    public static Position PlanePoint(Cavity model, string plane, double u, double v, double fraction)
    {
        return plane switch
        {
            "xy" => new(u * model.Length, v * model.Width, fraction * model.Height),
            "xz" => new(u * model.Length, fraction * model.Width, v * model.Height),
            "yz" => new(fraction * model.Length, u * model.Width, v * model.Height),
            _ => throw new ArgumentException("未知剖面")
        };
    }

    public static (double U, double V) Project(Position point, Cavity model, string plane)
    {
        return plane switch
        {
            "xy" => (point.X / model.Length, point.Y / model.Width),
            "xz" => (point.X / model.Length, point.Z / model.Height),
            "yz" => (point.Y / model.Width, point.Z / model.Height),
            _ => throw new ArgumentException("未知剖面")
        };
    }

    public static double Decibels(double value, double reference)
    {
        if (reference <= 1e-15)
            return -120;
        return Math.Max(-120, 20 * Math.Log10(Math.Max(value, reference * 1e-6) / reference));
    }

    public static string FieldColor(double value, double maximum, bool shape)
    {
        var t = maximum > 1e-15 ? Math.Clamp(value / maximum, shape ? -1 : 0, 1) : 0;
        if (shape)
        {
            var strength = Math.Abs(t);
            var target = t >= 0 ? (R: 204, G: 104, B: 56) : (R: 35, G: 117, B: 133);
            var r = (int)(245 + (target.R - 245) * strength);
            var g = (int)(241 + (target.G - 241) * strength);
            var b = (int)(225 + (target.B - 225) * strength);
            return $"rgb({r},{g},{b})";
        }
        return $"hsl({195 - 170 * t:F0},48%,{90 - 45 * t:F0}%)";
    }
}
