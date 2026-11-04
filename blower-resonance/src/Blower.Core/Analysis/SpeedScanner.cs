using Blower.Core.Domain;

namespace Blower.Core.Analysis;

public sealed record ScanChoice(int Position, string Gear, double OriginalRpm, double CandidateRpm, int Contacts, double Cost);

public sealed record ScanResult(
    bool Feasible,
    string Message,
    string InputHash,
    string PlanId,
    IReadOnlyList<ScanChoice> Choices,
    int EvaluatedPoints,
    double TotalCost);

public static class SpeedScanner
{
    private sealed record Node(double Rpm, int Contacts, double LocalCost, double Cost, int Previous);

    public static ScanResult Scan(Project project, string planId)
    {
        Validation.RequireValid(project, true);
        var plan = project.Plans.Single(x => x.Id == planId);
        var hash = IO.ProjectCodec.InputHash(project);
        var s = project.Settings;
        var modes = project.Modes.Where(x => x.Enabled).ToList();
        var orders = project.Orders.Where(x => x.Enabled).ToList();
        if (plan.Gears.Count == 0)
        {
            return new(false, "当前方案没有挡位", hash, planId, [], 0, 0);
        }
        var pointLimit = (int)Math.Ceiling(2 * s.ScanWindowRpm / s.ScanStepRpm) + 3;
        if (pointLimit > 603)
        {
            return new(false, "扫描窗口与步长形成超过603个候选/挡位；请增大步长或缩小窗口", hash, planId, [], 0, 0);
        }
        var levels = new List<List<Node>>();
        var evaluated = 0;
        for (var i = 0; i < plan.Gears.Count; i++)
        {
            var gear = plan.Gears[i];
            var lower = Math.Max(s.MinRpm, gear.Rpm - s.ScanWindowRpm);
            var upper = Math.Min(s.MaxRpm, gear.Rpm + s.ScanWindowRpm);
            var candidates = new SortedSet<double> { gear.Rpm };
            if (!gear.Fixed)
            {
                for (var rpm = Math.Ceiling(lower / s.ScanStepRpm) * s.ScanStepRpm;
                    rpm <= upper + 1e-8; rpm += s.ScanStepRpm)
                {
                    candidates.Add(Math.Round(rpm, 6));
                }
            }
            var level = new List<Node>();
            foreach (var rpm in candidates)
            {
                var probe = new Gear { Id = gear.Id, Name = gear.Name, Rpm = rpm };
                var count = 0;
                foreach (var order in orders)
                {
                    foreach (var mode in modes)
                    {
                        if (Engine.Evaluate(project, plan, probe, order, mode).Near)
                        {
                            count++;
                        }
                    }
                }
                evaluated++;
                var shift = Math.Abs(rpm - gear.Rpm) / Math.Max(1, s.ScanWindowRpm);
                var local = count + s.ShiftWeight * shift;
                var cost = i == 0 ? local : double.PositiveInfinity;
                var previous = -1;
                if (i > 0)
                {
                    for (var j = 0; j < levels[i - 1].Count; j++)
                    {
                        var parent = levels[i - 1][j];
                        if (rpm - parent.Rpm < s.MinimumGapRpm - 1e-8)
                        {
                            continue;
                        }
                        if (parent.Cost + local < cost - 1e-10)
                        {
                            cost = parent.Cost + local;
                            previous = j;
                        }
                    }
                }
                level.Add(new(rpm, count, local, cost, previous));
            }
            levels.Add(level);
        }
        var last = levels[^1];
        var selected = Enumerable.Range(0, last.Count).MinBy(x => last[x].Cost);
        if (!double.IsFinite(last[selected].Cost))
        {
            return new(false, "当前窗口、固定挡位与最小间距无法同时满足；请检查约束", hash, planId, [], evaluated, 0);
        }
        var total = last[selected].Cost;
        var choices = new List<ScanChoice>();
        for (var i = levels.Count - 1; i >= 0; i--)
        {
            var node = levels[i][selected];
            var gear = plan.Gears[i];
            choices.Add(new(i + 1, gear.Name, gear.Rpm, node.Rpm, node.Contacts, node.LocalCost));
            selected = node.Previous;
        }
        choices.Reverse();
        return new(true, "约束内离散候选已生成；需验证风量、效率与实际响应", hash, planId, choices, evaluated, total);
    }
}
