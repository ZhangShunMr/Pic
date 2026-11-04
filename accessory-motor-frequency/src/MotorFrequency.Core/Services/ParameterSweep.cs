using System.Globalization;
using System.Text;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public sealed class SweepRequest
{
    public List<int> PolePairs { get; set; } = [1, 2, 3];
    public List<int> Slots { get; set; } = [6, 12, 18];
    public List<int> Segments { get; set; } = [8, 10, 12];
    public double ReferenceRpm { get; set; } = 1800;
    public double ObservedHz { get; set; } = 60;
    public double ToleranceHz { get; set; } = 3;
}

public sealed record SweepRow(
    MotorTopology Topology,
    int PolePairs,
    int Slots,
    int Segments,
    string Candidate,
    double Order,
    double ReferenceRpm,
    double FrequencyHz,
    double ObservedHz,
    double DifferenceHz,
    bool WithinTolerance,
    string Basis);

public sealed record ReverseRow(
    string Candidate,
    double Order,
    double CenterRpm,
    double LowerRpm,
    double UpperRpm,
    bool WithinScan,
    string Basis);

public static class ParameterSweep
{
    public static string ContextHash(Project project)
    {
        var input = new
        {
            projectId = project.Id,
            projectName = project.Name,
            demonstration = project.Demonstration,
            motor = project.Motor,
            customOrders = project.CustomOrders
        };
        return ProjectCodec.Hash(System.Text.Json.JsonSerializer.Serialize(input, ProjectCodec.Options));
    }

    public static IReadOnlyList<int> ParseIntegers(string text, string field, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(field + "不能为空。");
        }
        var values = new List<int>();
        var tokens = text.Split(',', StringSplitOptions.TrimEntries);
        if (tokens.Length > 12)
        {
            throw new InvalidDataException(field + "最多输入 12 个整数，以英文逗号分隔。");
        }
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            {
                throw new InvalidDataException($"{field} 中的“{token}”无效，范围 {min}～{max}。");
            }
            if (values.Contains(value))
            {
                throw new InvalidDataException(field + "包含重复值 " + value + "，请删除重复项。");
            }
            values.Add(value);
        }
        return values.Order().ToList();
    }

    public static IReadOnlyList<ValidationIssue> Validate(SweepRequest request, MotorTopology topology)
    {
        var issues = new List<ValidationIssue>();
        if (!double.IsFinite(request.ReferenceRpm) || request.ReferenceRpm < 0 || request.ReferenceRpm > 100000)
        {
            issues.Add(new("referenceRpm", "参考转速需为 0～100000 r/min。"));
        }
        if (!double.IsFinite(request.ObservedHz) || request.ObservedHz < 0 || request.ObservedHz > 1000000)
        {
            issues.Add(new("observedHz", "观察频率需为 0～1000000 Hz。"));
        }
        if (!double.IsFinite(request.ToleranceHz) || request.ToleranceHz < 0 || request.ToleranceHz > 1000)
        {
            issues.Add(new("toleranceHz", "容差需为 0～1000 Hz。"));
        }
        void Check(IReadOnlyList<int>? list, string path, int minimum, int maximum)
        {
            if (list is null || list.Count is < 1 or > 12)
            {
                issues.Add(new(path, "每组需 1～12 个整数。"));
            }
            else if (list.Distinct().Count() != list.Count || list.Any(x => x < minimum || x > maximum))
            {
                issues.Add(new(path, "列表含重复项或超范围整数。"));
            }
        }
        if (topology == MotorTopology.BrushlessPermanentMagnet)
        {
            Check(request.PolePairs, "polePairs", 1, 64);
            Check(request.Slots, "slots", 1, 512);
        }
        else if (topology == MotorTopology.BrushedPermanentMagnet)
        {
            Check(request.Segments, "segments", 2, 512);
        }
        else
        {
            issues.Add(new("topology", "不支持的拓扑。"));
        }
        return issues;
    }

    public static IReadOnlyList<SweepRow> Calculate(Project project, SweepRequest request)
    {
        var errors = Validate(request, project.Motor.Topology);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("；", errors.Select(x => x.Path + "：" + x.Message)));
        }
        var result = new List<SweepRow>();
        var poles = project.Motor.Topology == MotorTopology.BrushlessPermanentMagnet ? request.PolePairs : [project.Motor.PolePairs];
        var slots = project.Motor.Topology == MotorTopology.BrushlessPermanentMagnet ? request.Slots : [project.Motor.Slots];
        var segments = project.Motor.Topology == MotorTopology.BrushedPermanentMagnet ? request.Segments : [project.Motor.CommutatorSegments];
        foreach (var pole in poles)
        {
            foreach (var slot in slots)
            {
                foreach (var segment in segments)
                {
                    var configuration = ProjectCodec.Clone(project.Motor);
                    configuration.PolePairs = pole;
                    configuration.Slots = slot;
                    configuration.CommutatorSegments = segment;
                    foreach (var candidate in FrequencyEngine.Candidates(configuration, project.CustomOrders).Where(x => x.Visible))
                    {
                        var frequency = FrequencyEngine.At(candidate.Order, request.ReferenceRpm);
                        var difference = frequency - request.ObservedHz;
                        result.Add(new(configuration.Topology, pole, slot, segment, candidate.Name, candidate.Order, request.ReferenceRpm, frequency, request.ObservedHz, difference, Math.Abs(difference) <= request.ToleranceHz, candidate.Basis));
                    }
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<ReverseRow> Reverse(Project project, double observedHz, double toleranceHz)
    {
        if (!double.IsFinite(observedHz) || observedHz < 0 || observedHz > 1000000)
        {
            throw new InvalidDataException("观察频率需为 0～1000000 Hz。");
        }
        if (!double.IsFinite(toleranceHz) || toleranceHz < 0 || toleranceHz > 1000)
        {
            throw new InvalidDataException("容差需为 0～1000 Hz。");
        }
        var output = new List<ReverseRow>();
        foreach (var candidate in FrequencyEngine.Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible))
        {
            var center = observedHz * 60 / candidate.Order;
            var lower = Math.Max(0, observedHz - toleranceHz) * 60 / candidate.Order;
            var upper = (observedHz + toleranceHz) * 60 / candidate.Order;
            var inScan = center >= project.Motor.MinimumRpm && center <= project.Motor.MaximumRpm;
            output.Add(new(candidate.Name, candidate.Order, center, lower, upper, inScan, candidate.Basis));
        }
        return output.OrderBy(x => x.CenterRpm).ToList();
    }

    public static string Csv(Project project, IReadOnlyList<SweepRow> rows)
    {
        var text = new StringBuilder("project,software_version,source,topology,pole_pairs,slots,segments,candidate,order,rpm,frequency_hz,observed_hz,delta_hz,within_tolerance,basis\n");
        string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        foreach (var row in rows)
        {
            text.AppendLine(string.Join(',', ExportService.CsvCell(project.Name), ExportService.CsvCell(Software.Version), ExportService.CsvCell(project.Demonstration ? "模拟演示" : "人工参数扫查"), ExportService.CsvCell(Labels.Topology(row.Topology)), row.PolePairs, row.Slots, row.Segments, ExportService.CsvCell(row.Candidate), N(row.Order), N(row.ReferenceRpm), N(row.FrequencyHz), N(row.ObservedHz), N(row.DifferenceHz), row.WithinTolerance ? "true" : "false", ExportService.CsvCell(row.Basis)));
        }
        return text.ToString();
    }
}
