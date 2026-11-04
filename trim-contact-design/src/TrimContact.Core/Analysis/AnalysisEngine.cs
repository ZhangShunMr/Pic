using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Analysis;

public static class AnalysisEngine
{
    public static AnalysisReport Run(DesignProject project, Action<int, string>? progress = null,
        CancellationToken cancellation = default)
    {
        ProjectValidation.Require(project, true);
        var report = new AnalysisReport
        {
            InputHash = ProjectCodec.InputHash(project),
            ProjectName = project.Name,
            Origin = project.Origin.Label(),
            Source = project.Source,
            Phases = project.Settings.Phases,
            ToleranceEnabled = project.Settings.EnumerateTolerance
        };
        for (int index = 0; index < project.Variants.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var variant = project.Variants[index];
            progress?.Invoke(index * 100 / project.Variants.Count, "求解 " + variant.Name);
            report.Variants.Add(RunVariant(project, variant, cancellation));
        }
        progress?.Invoke(100, "计算完成");
        return report;
    }

    public static VariantResult RunVariant(DesignProject project, DesignVariant variant,
        CancellationToken cancellation = default)
    {
        var solver = new ContactSolver(project.Panel, variant);
        var result = new VariantResult
        {
            VariantId = variant.Id,
            Name = variant.Name,
            Contacts = variant.Contacts.Select(c => new PointEnvelope
                { PointId = c.Id, Name = c.Name, Kind = "接触点" }).ToList(),
            Clips = variant.Clips.Select(c => new PointEnvelope
                { PointId = c.Id, Name = c.Name, Kind = "卡扣" }).ToList()
        };
        var toleranceCases = ToleranceCases(variant, project.Settings.EnumerateTolerance).ToArray();
        result.ToleranceCases = toleranceCases.Length;
        for (int cornerIndex = 0; cornerIndex < toleranceCases.Length; cornerIndex++)
        {
            var tolerance = toleranceCases[cornerIndex];
            foreach (var load in project.Loads)
            {
                cancellation.ThrowIfCancellationRequested();
                var trace = new TraceResult { LoadId = load.Id, LoadName = load.Name };
                for (int phaseIndex = 0; phaseIndex < project.Settings.Phases; phaseIndex++)
                {
                    var degrees = 360.0 * phaseIndex / project.Settings.Phases;
                    var radians = degrees * Math.PI / 180;
                    var normal = load.NormalBiasG + load.NormalAmplitudeG * Math.Sin(radians);
                    var shear = load.TangentialBiasG + load.TangentialAmplitudeG
                        * Math.Sin(radians + load.PhaseDegrees * Math.PI / 180);
                    var state = solver.Solve(degrees, normal, shear, tolerance);
                    var corner = cornerIndex == 0 ? "名义" : $"角点{cornerIndex}[{string.Join(",", tolerance.Select(t => t.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)))}]mm";
                    var condition = $"{load.Name} / {corner} / {degrees:F1}°";
                    for (int i = 0; i < result.Contacts.Count; i++)
                        result.Contacts[i].Accumulate(state.Contacts[i], condition, false);
                    for (int i = 0; i < result.Clips.Count; i++)
                        result.Clips[i].Accumulate(state.Clips[i], condition, true);
                    result.MaxResidualN = Math.Max(result.MaxResidualN, state.ResidualN);
                    result.EvaluatedStates++;
                    if (cornerIndex == 0)
                        trace.Samples.Add(state);
                }
                if (cornerIndex == 0)
                    result.NominalTraces.Add(trace);
            }
        }
        return result;
    }

    public static IEnumerable<double[]> ToleranceCases(DesignVariant variant, bool enabled)
    {
        yield return new double[variant.Contacts.Count];
        if (!enabled)
            yield break;
        var uncertain = variant.Contacts.Select((c, i) => (c, i)).Where(p => p.c.ToleranceMm > 0).ToArray();
        if (uncertain.Length == 0)
            yield break;
        for (int mask = 0; mask < (1 << uncertain.Length); mask++)
        {
            var values = new double[variant.Contacts.Count];
            for (int bit = 0; bit < uncertain.Length; bit++)
                values[uncertain[bit].i] = ((mask & (1 << bit)) == 0 ? -1 : 1) * uncertain[bit].c.ToleranceMm;
            yield return values;
        }
    }

    public static IReadOnlyList<SweepRow> Sweep(DesignProject project, Guid variantId, Guid pointId,
        string parameter, double from, double to, int steps, CancellationToken cancellation = default)
    {
        if (steps is < 2 or > 15 || !double.IsFinite(from) || !double.IsFinite(to) || to <= from)
            throw new InvalidDataException("扫描需要2至15个递增的有限参数值");
        var clone = ProjectCodec.Clone(project);
        var variant = clone.Variants.Single(v => v.Id == variantId);
        clone.Variants = [variant];
        // Verification tasks do not affect mechanics and may refer to other variants.
        clone.Tasks.Clear();
        var point = variant.Contacts.SingleOrDefault(c => c.Id == pointId)
            ?? throw new InvalidDataException("先选择一个接触点");
        var rows = new List<SweepRow>();
        for (int i = 0; i < steps; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var value = from + (to - from) * i / (steps - 1);
            switch (parameter)
            {
                case "预紧量mm": point.PreloadMm = value; break;
                case "间隙mm": point.GapMm = value; break;
                case "法向刚度N/mm": point.NormalStiffness = value; break;
                case "摩擦系数": point.Friction = value; break;
                default: throw new InvalidDataException("不支持的扫描参数");
            }
            ProjectValidation.Require(clone, true);
            var result = RunVariant(clone, variant, cancellation);
            rows.Add(new(value, result.SeparationPoints, result.SlidingPoints, result.OverLimitPoints,
                result.Clips.Max(c => c.MaxUtilization), result.Contacts.Max(c => c.MaxCompressionMm),
                result.Contacts.Max(c => c.MaxSlipMm)));
        }
        return rows;
    }

    public static IReadOnlyList<ComparisonRow> Compare(AnalysisReport report) => report.Variants
        .Select(v => new ComparisonRow(v.Name, v.SeparationPoints, v.SlidingPoints, v.OverLimitPoints,
            v.Clips.Max(c => c.MaxUtilization), v.Contacts.Select(c => c.MaxSlipMm).DefaultIfEmpty().Max()))
        .ToArray();
}
