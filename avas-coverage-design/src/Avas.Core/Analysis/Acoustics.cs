using Avas.Core.Domain;

namespace Avas.Core.Analysis;

public static class Acoustics
{
    public static double Sum(IEnumerable<double> levels)
    {
        var values = levels.ToArray();
        if (values.Length == 0)
            return double.NegativeInfinity;
        var max = values.Max();
        if (double.IsNegativeInfinity(max))
            return max;
        return max + 10 * Math.Log10(values.Sum(x => Math.Pow(10, (x - max) / 10)));
    }

    public static double DirectionalLoss(Speaker speaker, double x, double y)
    {
        if (speaker.Exponent == 0)
            return 0;
        var angle = Math.Atan2(y - speaker.Y, x - speaker.X) - speaker.Heading * Math.PI / 180;
        var forward = Math.Max(0, Math.Cos(angle));
        var amplitude = speaker.RearFloor + (1 - speaker.RearFloor) * Math.Pow(forward, speaker.Exponent);
        return 20 * Math.Log10(amplitude);
    }

    public static double Distance(Speaker speaker, double x, double y, double height)
    {
        return Math.Sqrt(Math.Pow(x - speaker.X, 2) + Math.Pow(y - speaker.Y, 2)
            + Math.Pow(height - speaker.Height, 2));
    }

    public static double Propagation(Speaker speaker, Settings settings, double x, double y)
    {
        var distance = Math.Max(settings.MinimumDistance, Distance(speaker, x, y, settings.ReceiverHeight));
        return -20 * Math.Log10(distance / speaker.ReferenceDistance) + DirectionalLoss(speaker, x, y);
    }

    public static double Select(IReadOnlyList<double> bands, Settings settings)
    {
        if (settings.SelectedHz != 0)
            return bands[Array.IndexOf(Software.Frequencies, settings.SelectedHz)];
        return Sum(bands.Select((level, index) => level + (settings.AWeighted ? Software.AWeights[index] : 0)));
    }

    public static bool InsideVehicle(Settings settings, double x, double y)
    {
        return Math.Abs(x) <= settings.VehicleLength / 2 && Math.Abs(y) <= settings.VehicleWidth / 2;
    }

    public static string Metric(Settings settings)
    {
        if (settings.SelectedHz != 0)
            return $"{settings.SelectedHz}Hz频带声级 dB re 20µPa";
        return settings.AWeighted ? "七带A计权合成 dB(A)" : "七带非计权合成 dB re 20µPa";
    }
}

public sealed record SourceContribution(Guid Id, string Name, double Distance, double Loss, double[] Bands, string Origin);

public sealed class PointResult
{
    public Guid? ListenerId { get; init; }
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double[] Bands { get; init; } = [];
    public double Signal { get; init; }
    public double Background { get; init; }
    public double Combined { get; init; }
    public double Margin => Signal - Background;
    public bool Covered { get; init; }
    public bool NearFieldClamped { get; init; }
    public bool InVehicle { get; init; }
    public List<SourceContribution> Contributions { get; init; } = [];
}

public sealed class Calculation
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string InputHash { get; init; } = "";
    public string Metric { get; init; } = "";
    public List<PointResult> Grid { get; init; } = [];
    public List<PointResult> Listeners { get; init; } = [];
    public int CoveredCount => Grid.Count(x => x.Covered);
    public double Coverage => Grid.Count == 0 ? 0 : 100.0 * CoveredCount / Grid.Count;
    public int ClampedCount => Grid.Count(x => x.NearFieldClamped);
    public double Minimum => Grid.Count == 0 ? 0 : Grid.Min(x => x.Signal);
    public double Maximum => Grid.Count == 0 ? 0 : Grid.Max(x => x.Signal);
    public double MeanEnergy => Acoustics.Sum(Grid.Select(x => x.Signal)) - 10 * Math.Log10(Grid.Count);
    public List<string> Warnings { get; init; } = [];
}

public static class FieldEngine
{
    public static Calculation Calculate(Project project)
    {
        var issues = Validation.Check(project, true);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("\n", issues));
        var settings = project.Settings;
        var nx = (int)Math.Floor((settings.MaxX - settings.MinX) / settings.Step + 1e-9);
        var ny = (int)Math.Floor((settings.MaxY - settings.MinY) / settings.Step + 1e-9);
        var grid = new List<PointResult>();
        for (var ix = 0; ix <= nx; ix++)
        {
            for (var iy = 0; iy <= ny; iy++)
            {
                var x = settings.MinX + ix * settings.Step;
                var y = settings.MinY + iy * settings.Step;
                if (settings.ExcludeVehicle && Acoustics.InsideVehicle(settings, x, y))
                    continue;
                grid.Add(Evaluate(project, x, y));
            }
        }
        if (grid.Count == 0)
            throw new ArgumentException("所有网格均位于排除的车辆内部；请扩大区域或关闭内部排除");
        var warnings = new List<string> { Software.Boundary };
        if (grid.Any(x => x.NearFieldClamped))
            warnings.Add("部分节点距离小于距离下限，使用截断距离；近源值不具备近场物理意义。");
        if (project.Speakers.Any(x => x.Origin == Origin.Simulation) || settings.BackgroundOrigin == Origin.Simulation)
            warnings.Add("包含模拟数据，不能证明真实应用效果。");
        if (project.Speakers.Count(x => x.Enabled) > 1)
            warnings.Add("多声源采用非相干能量叠加；相同信号的相位干涉未计算。");
        if (project.Listeners.Any(x => x.X < settings.MinX || x.X > settings.MaxX || x.Y < settings.MinY || x.Y > settings.MaxY))
            warnings.Add("部分观察点位于网格范围外，点值仍独立计算，不计入网格覆盖率。");
        return new Calculation
        {
            InputHash = IO.ProjectCodec.InputHash(project),
            Metric = Acoustics.Metric(settings),
            Grid = grid,
            Listeners = project.Listeners.Select(x => Evaluate(project, x.X, x.Y, x.Name, x.Id)).ToList(),
            Warnings = warnings
        };
    }

    public static PointResult Evaluate(Project project, double x, double y, string name = "网格节点", Guid? id = null)
    {
        var settings = project.Settings;
        var sources = new List<SourceContribution>();
        var clamped = false;
        foreach (var speaker in project.Speakers.Where(s => s.Enabled))
        {
            var distance = Acoustics.Distance(speaker, x, y, settings.ReceiverHeight);
            clamped |= distance < settings.MinimumDistance;
            var loss = Acoustics.Propagation(speaker, settings, x, y);
            var bands = Software.Frequencies.Select(hz =>
            {
                var band = speaker.Bands.Single(b => b.Hz == hz);
                return band.Level + band.Correction + speaker.Gain + loss;
            }).ToArray();
            sources.Add(new(speaker.Id, speaker.Name, distance, loss, bands, speaker.Origin.Label() + " / " + speaker.Source));
        }
        var combinedBands = Software.Frequencies.Select((_, i) => Acoustics.Sum(sources.Select(s => s.Bands[i]))).ToArray();
        var backgroundBands = Software.Frequencies.Select(hz => settings.Background.Single(b => b.Hz == hz).Level).ToArray();
        var signal = Acoustics.Select(combinedBands, settings);
        var background = Acoustics.Select(backgroundBands, settings);
        return new PointResult
        {
            ListenerId = id,
            Name = name,
            X = x,
            Y = y,
            Bands = combinedBands,
            Signal = signal,
            Background = background,
            Combined = Acoustics.Sum([signal, background]),
            Covered = signal >= settings.LevelTarget && signal - background >= settings.MarginTarget,
            NearFieldClamped = clamped,
            InVehicle = Acoustics.InsideVehicle(settings, x, y),
            Contributions = sources
        };
    }
}
