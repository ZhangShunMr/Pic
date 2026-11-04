namespace Blower.Core.Domain;

public sealed record Issue(string Path, string Message, bool Blocks = true);

public static class Validation
{
    public static IReadOnlyList<Issue> Inspect(Project project, bool forAnalysis = false)
    {
        var issues = new List<Issue>();
        void Required(string value, string path, int max = 160)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new(path, "不能为空"));
            }
            else if (value.Length > max)
            {
                issues.Add(new(path, $"不得超过{max}个字符"));
            }
        }
        void Range(double value, double min, double max, string path)
        {
            if (!double.IsFinite(value) || value < min || value > max)
            {
                issues.Add(new(path, $"必须在{min}至{max}之间，且为有限数值"));
            }
        }
        if (project.Schema != Software.Schema)
        {
            issues.Add(new("格式版本", "仅支持schema=1；请使用对应版本另存"));
        }
        Required(project.Id, "项目标识");
        if (project.Id.Length != 32 || project.Id.Any(x => !Uri.IsHexDigit(x)))
        {
            issues.Add(new("项目标识", "必须为32位十六进制"));
        }
        Required(project.Name, "项目名称");
        Required(project.Source, "项目来源", 2000);
        if (!Enum.IsDefined(project.Origin))
        {
            issues.Add(new("数据来源", "未知枚举值"));
        }
        var s = project.Settings;
        Range(s.Blades, 1, 200, "叶片数");
        Range(s.MinRpm, 0, 50000, "转速下限");
        Range(s.MaxRpm, 1, 50000, "转速上限");
        if (s.MaxRpm <= s.MinRpm)
        {
            issues.Add(new("转速范围", "上限必须大于下限"));
        }
        Range(s.AbsoluteBandHz, 0, 5000, "绝对半宽Hz");
        Range(s.RelativeBandPercent, 0, 30, "百分比半宽%");
        Range(s.SpeedToleranceRpm, 0, 1000, "转速容差rpm");
        Range(s.MinimumGapRpm, 1, 5000, "最小挡位间距rpm");
        Range(s.ScanStepRpm, 1, 500, "扫描步长rpm");
        Range(s.ScanWindowRpm, 0, 3000, "扫描窗口±rpm");
        Range(s.ShiftWeight, 0, 1, "偏移权重");
        if (project.Orders.Count > 24 || project.Modes.Count > 100 || project.Plans.Count > 20)
        {
            issues.Add(new("对象数量", "最多24个阶次、100个模态和20个方案"));
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Unique(string id, string path)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                issues.Add(new(path, "标识为空或重复"));
            }
        }
        var effectiveOrders = new HashSet<double>();
        foreach (var order in project.Orders)
        {
            var path = $"阶次[{order.Name}]";
            Unique(order.Id, path);
            Required(order.Name, path + ".名称");
            Range(order.Multiplier, 0.1, 100, path + ".倍数");
            if (!Enum.IsDefined(order.Kind))
            {
                issues.Add(new(path + ".类别", "未知阶次类别"));
            }
            if (order.Enabled && !effectiveOrders.Add(order.EffectiveOrder(s.Blades)))
            {
                issues.Add(new(path, "存在重复的启用有效阶次；请合并或停用其中一项，避免组合重复计数"));
            }
        }
        foreach (var mode in project.Modes)
        {
            var path = $"模态[{mode.Name}]";
            Unique(mode.Id, path);
            Required(mode.Name, path + ".名称");
            Required(mode.Source, path + ".来源", 2000);
            Range(mode.FrequencyHz, 0.1, 50000, path + ".频率Hz");
            Range(mode.ExtraBandHz, 0, 5000, path + ".附加半宽Hz");
            if (mode.DampingRatio.HasValue)
            {
                Range(mode.DampingRatio.Value, 0, 0.1, path + ".阻尼比ζ");
            }
            if (!Enum.IsDefined(mode.Origin))
            {
                issues.Add(new(path + ".来源类别", "未知枚举值"));
            }
        }
        foreach (var plan in project.Plans)
        {
            var path = $"方案[{plan.Name}]";
            Unique(plan.Id, path);
            Required(plan.Name, path + ".名称");
            if (plan.Gears.Count > 24)
            {
                issues.Add(new(path, "最多24个挡位"));
            }
            var previous = double.NegativeInfinity;
            foreach (var gear in plan.Gears)
            {
                Unique(gear.Id, path + ".挡位");
                Required(gear.Name, path + ".挡位名称");
                Range(gear.Rpm, s.MinRpm, s.MaxRpm, path + $".{gear.Name}.rpm");
                if (gear.Rpm - previous < s.MinimumGapRpm - 1e-8)
                {
                    issues.Add(new(path + $".{gear.Name}", "挡位须按转速递增排列并满足最小间距"));
                }
                previous = gear.Rpm;
            }
        }
        if (project.Checks.Count > 2000 || project.Audit.Count > 5000)
        {
            issues.Add(new("记录数量", "验证记录最多2000条，审计最多5000条"));
        }
        var taskKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in project.Checks)
        {
            Unique(task.Id, "验证记录");
            var expectedKey = string.Join("/", task.PlanId, task.GearId, task.OrderId, task.ModeId);
            if (task.Key != expectedKey || !taskKeys.Add(task.Key))
            {
                issues.Add(new("验证记录." + task.Title, "组合标识不一致或重复"));
            }
            var plan = project.Plans.FirstOrDefault(x => x.Id == task.PlanId);
            if (plan == null || !plan.Gears.Any(x => x.Id == task.GearId)
                || !project.Modes.Any(x => x.Id == task.ModeId)
                || !project.Orders.Any(x => x.Id == task.OrderId))
            {
                issues.Add(new("验证记录." + task.Title, "关联对象缺失；应先移除对应验证记录"));
            }
            if (!Enum.IsDefined(task.State))
            {
                issues.Add(new("验证状态", "未知枚举值"));
            }
            if (task.State == CheckState.Completed && string.IsNullOrWhiteSpace(task.Evidence))
            {
                issues.Add(new("验证记录." + task.Title, "已记录证据状态必须填写证据位置或结论"));
            }
        }
        if (forAnalysis)
        {
            if (!project.Orders.Any(x => x.Enabled))
            {
                issues.Add(new("阶次", "至少启用一个阶次"));
            }
            if (!project.Modes.Any(x => x.Enabled))
            {
                issues.Add(new("模态", "至少启用一个模态"));
            }
            if (!project.Plans.Any(x => x.Gears.Count > 0))
            {
                issues.Add(new("挡位方案", "至少建立一个含挡位的方案"));
            }
        }
        if (string.IsNullOrWhiteSpace(project.Owner))
        {
            issues.Add(new("负责人", "建议补充以便追溯", false));
        }
        return issues;
    }

    public static void RequireValid(Project project, bool forAnalysis = false)
    {
        var errors = Inspect(project, forAnalysis).Where(x => x.Blocks).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("；", errors.Select(x => x.Path + "：" + x.Message)));
        }
    }
}
