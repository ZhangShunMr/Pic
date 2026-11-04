using System.Globalization;
using Blower.Core.Domain;
using Blower.Core.IO;

namespace Blower.Core.Analysis;

public sealed record RuleCase(
    double AbsoluteBandHz,
    double SpeedToleranceRpm,
    int NearGears,
    int Contacts,
    int ChangedStatuses,
    double MinimumClearanceHz,
    string GearNames);

public sealed class RuleStudyResult
{
    public string InputHash { get; init; } = "";
    public string PlanId { get; init; } = "";
    public string PlanName { get; init; } = "";
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
    public List<RuleCase> Cases { get; init; } = [];
}

public static class RuleStudy
{
    public static IReadOnlyList<double> ParseValues(string text, double maximum, string field)
    {
        var values = new List<double>();
        foreach (var cell in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number)
                || number < 0
                || number > maximum)
            {
                throw new InvalidDataException(field + $"只接受0～{maximum}的数值，用英文逗号分隔");
            }
            if (!values.Contains(number))
            {
                values.Add(number);
            }
        }
        if (values.Count is < 1 or > 5)
        {
            throw new InvalidDataException(field + "需要1～5个不同值");
        }
        return values.Order().ToList();
    }

    public static RuleStudyResult Run(Project project, string planId, string absoluteValues, string toleranceValues)
    {
        Validation.RequireValid(project, true);
        var plan = project.Plans.Single(x => x.Id == planId);
        if (plan.Gears.Count == 0)
        {
            throw new InvalidOperationException("当前方案没有挡位");
        }
        var baseline = Engine.Run(project);
        var original = baseline.Contacts.Where(x => x.PlanId == planId).ToDictionary(x => x.Key);
        var widths = ParseValues(absoluteValues, 5000, "绝对半宽");
        var tolerances = ParseValues(toleranceValues, 1000, "转速容差");
        var cases = new List<RuleCase>();
        foreach (var width in widths)
        {
            foreach (var tolerance in tolerances)
            {
                var candidate = ProjectCodec.Clone(project);
                candidate.Settings.AbsoluteBandHz = width;
                candidate.Settings.SpeedToleranceRpm = tolerance;
                var result = Engine.Run(candidate);
                var summary = result.Summaries.Single(x => x.PlanId == planId);
                var contacts = result.Contacts.Where(x => x.PlanId == planId).ToList();
                var changes = contacts.Count(x => x.Near != original[x.Key].Near);
                var gears = string.Join("、", summary.Gears.Where(x => x.Contacts > 0).Select(x => x.Name));
                cases.Add(new(width, tolerance, summary.NearGearCount, summary.ContactCount,
                    changes, summary.MinimumClearanceHz ?? 0, gears.Length == 0 ? "无接近挡位" : gears));
            }
        }
        return new()
        {
            InputHash = baseline.InputHash,
            PlanId = planId,
            PlanName = plan.Name,
            Cases = cases
        };
    }

    public static string ToCsv(Project project, RuleStudyResult study)
    {
        if (study.InputHash != ProjectCodec.InputHash(project))
        {
            throw new InvalidOperationException("规则敏感性试算已过期；请重新试算");
        }
        var rows = new List<string>
        {
            CsvExchange.Row("project", "source", "input_sha256", "plan", "absolute_half_width_hz",
                "speed_tolerance_rpm", "near_gears", "contacts", "changed_statuses", "minimum_clearance_hz", "gear_names")
        };
        foreach (var item in study.Cases)
        {
            rows.Add(CsvExchange.Row(project.Name, Labels.Source(project.Origin), study.InputHash,
                study.PlanName, CsvExchange.Number(item.AbsoluteBandHz), CsvExchange.Number(item.SpeedToleranceRpm),
                item.NearGears.ToString(), item.Contacts.ToString(), item.ChangedStatuses.ToString(),
                CsvExchange.Number(item.MinimumClearanceHz), item.GearNames));
        }
        return string.Join(Environment.NewLine, rows);
    }
}
