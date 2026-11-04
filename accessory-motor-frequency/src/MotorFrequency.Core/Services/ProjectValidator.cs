using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class ProjectValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(Project? project)
    {
        var issues = new List<ValidationIssue>();
        if (project is null)
        {
            issues.Add(new("project", "项目不能为空。"));
            return issues;
        }
        if (project.FormatVersion != Software.FormatVersion)
        {
            issues.Add(new("formatVersion", "不支持的项目格式版本。"));
        }
        if (project.Id == Guid.Empty)
        {
            issues.Add(new("id", "项目标识不能为空。"));
        }
        Text(issues, "softwareVersion", project.SoftwareVersion, 32, true);
        Text(issues, "name", project.Name, 100, true);
        Text(issues, "owner", project.Owner, 100, false);
        Text(issues, "purpose", project.Purpose, 2000, false);
        issues.AddRange(ValidateMotor(project.Motor));
        if (project.Review is null)
        {
            issues.Add(new("review", "复核设置缺失。"));
        }
        else
        {
            Range(issues, "review.matchToleranceHz", project.Review.MatchToleranceHz, 0, 1000);
            if (project.Review.MaximumPeaks is < 1 or > 50)
            {
                issues.Add(new("review.maximumPeaks", "谱峰数量需为 1～50。"));
            }
            Text(issues, "review.notes", project.Review.Notes, 4000, false);
        }
        ValidateCollections(project, issues);
        return issues;
    }

    public static IReadOnlyList<ValidationIssue> ValidateMotor(MotorConfiguration? motor)
    {
        var issues = new List<ValidationIssue>();
        if (motor is null)
        {
            issues.Add(new("motor", "电机参数缺失。"));
            return issues;
        }
        Text(issues, "motor.name", motor.Name, 100, true);
        if (!Enum.IsDefined(motor.Topology))
        {
            issues.Add(new("motor.topology", "不支持的电机拓扑。"));
        }
        Integer(issues, "motor.polePairs", motor.PolePairs, 1, 64);
        Integer(issues, "motor.slots", motor.Slots, 1, 512);
        Integer(issues, "motor.commutatorSegments", motor.CommutatorSegments, 2, 512);
        Range(issues, "motor.minimumRpm", motor.MinimumRpm, 0, 100000);
        Range(issues, "motor.maximumRpm", motor.MaximumRpm, 0, 100000);
        Range(issues, "motor.stepRpm", motor.StepRpm, 0.1, 100000);
        Range(issues, "motor.inspectRpm", motor.InspectRpm, 0, 100000);
        Range(issues, "motor.maximumHz", motor.MaximumHz, 1, 1000000);
        if (motor.MaximumRpm <= motor.MinimumRpm)
        {
            issues.Add(new("motor.maximumRpm", "终止转速必须大于起始转速。"));
        }
        if (motor.InspectRpm < motor.MinimumRpm || motor.InspectRpm > motor.MaximumRpm)
        {
            issues.Add(new("motor.inspectRpm", "观察转速必须位于扫描范围内。"));
        }
        if (motor.StepRpm > 0 && (motor.MaximumRpm - motor.MinimumRpm) / motor.StepRpm > 2000)
        {
            issues.Add(new("motor.stepRpm", "扫描区间最多 2000 个步长，请增大步长。"));
        }
        if (motor.IncludeIdealCogging && motor.Topology == MotorTopology.BrushlessPermanentMagnet)
        {
            if (!motor.SymmetricUnskewedAssumption || string.IsNullOrWhiteSpace(motor.AssumptionNotes))
            {
                issues.Add(new("motor.includeIdealCogging", "启用理想齿槽候选前，须确认等槽对称假设并填写依据。"));
            }
        }
        Text(issues, "motor.assumptionNotes", motor.AssumptionNotes, 2000, false);
        if (motor.HiddenCandidates is null || motor.HiddenCandidates.Count > 100)
        {
            issues.Add(new("motor.hiddenCandidates", "隐藏候选记录缺失或超过 100 条。"));
        }
        return issues;
    }

    private static void ValidateCollections(Project p, List<ValidationIssue> issues)
    {
        if (p.CustomOrders is null || p.Bands is null || p.Spectra is null || p.Scenarios is null || p.History is null)
        {
            issues.Add(new("collections", "候选、频段、频谱、快照及历史集合均须存在。"));
            return;
        }
        Limit(issues, "customOrders", p.CustomOrders.Count, 40);
        Limit(issues, "bands", p.Bands.Count, 30);
        Limit(issues, "spectra", p.Spectra.Count, 12);
        Limit(issues, "scenarios", p.Scenarios.Count, 12);
        Limit(issues, "history", p.History.Count, 2000);
        Unique(issues, "customOrders", p.CustomOrders.Where(x => x is not null).Select(x => x.Id));
        Unique(issues, "bands", p.Bands.Where(x => x is not null).Select(x => x.Id));
        Unique(issues, "spectra", p.Spectra.Where(x => x is not null).Select(x => x.Id));
        Unique(issues, "scenarios", p.Scenarios.Where(x => x is not null).Select(x => x.Id));
        foreach (var order in p.CustomOrders)
        {
            if (order is null)
            {
                issues.Add(new("customOrders", "候选对象不能为空。"));
                continue;
            }
            ValidateOrder(order, "customOrders/" + order.Id, issues);
        }
        foreach (var band in p.Bands)
        {
            if (band is null)
            {
                issues.Add(new("bands", "敏感频段对象不能为空。"));
                continue;
            }
            var path = "bands/" + band.Id;
            Text(issues, path + "/name", band.Name, 100, true);
            Text(issues, path + "/basis", band.Basis, 2000, true);
            Range(issues, path + "/lowerHz", band.LowerHz, 0, 1000000);
            Range(issues, path + "/upperHz", band.UpperHz, 0, 1000000);
            if (band.UpperHz < band.LowerHz)
            {
                issues.Add(new(path, "频段上限不能小于下限。"));
            }
            if (!Enum.IsDefined(band.Origin))
            {
                issues.Add(new(path + "/origin", "未知来源分类。"));
            }
        }
        ValidateHidden(p.Motor, p.CustomOrders, "motor.hiddenCandidates", issues);
        var totalPoints = 0;
        foreach (var spectrum in p.Spectra)
        {
            if (spectrum is null)
            {
                issues.Add(new("spectra", "频谱对象不能为空。"));
                continue;
            }
            issues.AddRange(ValidateSpectrum(spectrum));
            totalPoints += spectrum.Points?.Count ?? 0;
        }
        if (totalPoints > 30000)
        {
            issues.Add(new("spectra", "项目内频谱点总数不能超过 30000。"));
        }
        foreach (var scenario in p.Scenarios)
        {
            if (scenario is null)
            {
                issues.Add(new("scenarios", "快照不能为空。"));
                continue;
            }
            var path = "scenarios/" + scenario.Id;
            Text(issues, path + "/name", scenario.Name, 100, true);
            issues.AddRange(ValidateMotor(scenario.Motor).Select(x => new ValidationIssue(path + "/" + x.Path, x.Message)));
            if (scenario.CustomOrders is null || scenario.CustomOrders.Count > 40)
            {
                issues.Add(new(path + "/customOrders", "快照候选缺失或超出限制。"));
            }
            else
            {
                Unique(issues, path + "/customOrders", scenario.CustomOrders.Where(x => x is not null).Select(x => x.Id));
                ValidateHidden(scenario.Motor, scenario.CustomOrders, path + "/motor.hiddenCandidates", issues);
                foreach (var order in scenario.CustomOrders)
                {
                    if (order is null)
                    {
                        issues.Add(new(path, "快照候选不能为空。"));
                    }
                    else
                    {
                        ValidateOrder(order, path, issues);
                    }
                }
            }
        }
        foreach (var item in p.History)
        {
            if (item is null)
            {
                issues.Add(new("history", "历史事件不能为空。"));
                continue;
            }
            Text(issues, "history/action", item.Action, 100, true);
            Text(issues, "history/detail", item.Detail, 4000, false);
        }
    }

    private static void ValidateHidden(MotorConfiguration? motor, IReadOnlyList<CustomOrder> custom, string path, List<ValidationIssue> issues)
    {
        if (motor?.HiddenCandidates is null)
        {
            return;
        }
        var allowed = new HashSet<string> { "rotation", "electrical", "slot", "cogging", "commutator" };
        foreach (var item in custom.Where(x => x is not null))
        {
            allowed.Add("custom:" + item.Id);
        }
        if (motor.HiddenCandidates.Distinct().Count() != motor.HiddenCandidates.Count
            || motor.HiddenCandidates.Any(x => x is null || !allowed.Contains(x)))
        {
            issues.Add(new(path, "隐藏候选记录包含重复或无效引用。"));
        }
    }

    private static void ValidateOrder(CustomOrder order, string path, List<ValidationIssue> issues)
    {
        Text(issues, path + "/name", order.Name, 100, true);
        Text(issues, path + "/basis", order.Basis, 2000, true);
        Range(issues, path + "/order", order.Order, 0.001, 10000);
    }

    public static IReadOnlyList<ValidationIssue> ValidateSpectrum(Spectrum spectrum)
    {
        var issues = new List<ValidationIssue>();
        var path = "spectra/" + spectrum.Id;
        Text(issues, path + "/name", spectrum.Name, 100, true);
        Text(issues, path + "/fileName", spectrum.FileName, 255, false);
        Text(issues, path + "/reference", spectrum.Reference, 200, true);
        Text(issues, path + "/condition", spectrum.Condition, 1000, true);
        Range(issues, path + "/rpm", spectrum.Rpm, 0, 100000);
        if (!Enum.IsDefined(spectrum.Origin))
        {
            issues.Add(new(path + "/origin", "未知来源分类。"));
        }
        if (spectrum.Unit is not ("dB" or "Pa" or "m/s²" or "g" or "A" or "V" or "任意单位"))
        {
            issues.Add(new(path + "/unit", "请选择支持的频谱单位。"));
        }
        if (spectrum.InputSha256 is null || spectrum.InputSha256.Length != 64
            || !spectrum.InputSha256.All(Uri.IsHexDigit))
        {
            issues.Add(new(path + "/inputSha256", "导入文本SHA-256需为64位十六进制字符串。"));
        }
        if (spectrum.Points is null || spectrum.Points.Count < 2 || spectrum.Points.Count > 10000)
        {
            issues.Add(new(path + "/points", "每条频谱需 2～10000 个点。"));
            return issues;
        }
        double previous = -1;
        for (var i = 0; i < spectrum.Points.Count; i++)
        {
            var point = spectrum.Points[i];
            if (point is null)
            {
                issues.Add(new(path + "/points/" + i, "频谱点不能为空。"));
                continue;
            }
            Range(issues, path + $"/points/{i}/frequencyHz", point.FrequencyHz, 0, 1000000);
            Range(issues, path + $"/points/{i}/amplitude", point.Amplitude, -1000000000, 1000000000);
            if (point.FrequencyHz <= previous)
            {
                issues.Add(new(path + $"/points/{i}", "频率必须严格递增，无重复。"));
            }
            if (spectrum.Unit != "dB" && point.Amplitude < 0)
            {
                issues.Add(new(path + $"/points/{i}", "线性幅值频谱不接受负值。"));
            }
            previous = point.FrequencyHz;
            if (issues.Count > 25)
            {
                issues.Add(new(path, "错误过多，停止继续列出；整条频谱不导入。"));
                break;
            }
        }
        return issues;
    }

    public static void ThrowIfInvalid(Project project)
    {
        var issues = Validate(project);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join("\n", issues.Take(25).Select(x => x.Path + "：" + x.Message)));
        }
    }

    private static void Text(List<ValidationIssue> issues, string path, string? value, int max, bool required)
    {
        if (value is null || (required && string.IsNullOrWhiteSpace(value)))
        {
            issues.Add(new(path, "字段必须填写。"));
        }
        else if (value.Length > max)
        {
            issues.Add(new(path, $"长度不能超过 {max} 字符。"));
        }
    }

    private static void Range(List<ValidationIssue> issues, string path, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            issues.Add(new(path, $"请输入 {Labels.Number(min)}～{Labels.Number(max)} 范围的有限数值。"));
        }
    }

    private static void Integer(List<ValidationIssue> issues, string path, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            issues.Add(new(path, $"请输入 {min}～{max} 的整数。"));
        }
    }

    private static void Limit(List<ValidationIssue> issues, string path, int count, int max)
    {
        if (count > max)
        {
            issues.Add(new(path, $"对象数量超过上限 {max}。"));
        }
    }

    private static void Unique(List<ValidationIssue> issues, string path, IEnumerable<Guid> ids)
    {
        var values = ids.ToList();
        if (values.Any(x => x == Guid.Empty) || values.Distinct().Count() != values.Count)
        {
            issues.Add(new(path, "对象标识为空或重复。"));
        }
    }
}
