using Blower.Core.Domain;

namespace Blower.Core.Analysis;

public sealed record GearDifference(
    int Position,
    string BaselineGear,
    string TargetGear,
    double? BaselineRpm,
    double? TargetRpm,
    double? DeltaRpm,
    int? BaselineContacts,
    int? TargetContacts,
    string Note);

public sealed record PlanComparison(
    string Baseline,
    string Target,
    int BaselineNear,
    int TargetNear,
    int BaselineContacts,
    int TargetContacts,
    bool EqualGearCount,
    IReadOnlyList<GearDifference> Rows);

public static class Comparisons
{
    public static PlanComparison Compare(Project project, AnalysisResult result, string baselineId, string targetId)
    {
        if (result.InputHash != IO.ProjectCodec.InputHash(project))
        {
            throw new InvalidOperationException("输入已变化，请重新计算后比较");
        }
        if (baselineId == targetId)
        {
            throw new InvalidOperationException("请选择两个不同方案");
        }
        var baseline = project.Plans.Single(x => x.Id == baselineId);
        var target = project.Plans.Single(x => x.Id == targetId);
        var a = result.Summaries.Single(x => x.PlanId == baselineId);
        var b = result.Summaries.Single(x => x.PlanId == targetId);
        var rows = new List<GearDifference>();
        for (var i = 0; i < Math.Max(baseline.Gears.Count, target.Gears.Count); i++)
        {
            var ga = baseline.Gears.ElementAtOrDefault(i);
            var gb = target.Gears.ElementAtOrDefault(i);
            rows.Add(new(i + 1, ga?.Name ?? "缺失", gb?.Name ?? "缺失",
                ga?.Rpm, gb?.Rpm,
                ga == null || gb == null ? null : gb.Rpm - ga.Rpm,
                ga == null ? null : a.Gears.Single(x => x.GearId == ga.Id).Contacts,
                gb == null ? null : b.Gears.Single(x => x.GearId == gb.Id).Contacts,
                ga == null || gb == null ? "缺挡位，不形成转速差" : "按方案内挡位序号比较"));
        }
        return new(baseline.Name, target.Name, a.NearGearCount, b.NearGearCount,
            a.ContactCount, b.ContactCount, baseline.Gears.Count == target.Gears.Count, rows);
    }
}
