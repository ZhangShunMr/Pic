using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class FrequencyEngine
{
    public static IReadOnlyList<Candidate> Candidates(MotorConfiguration motor, IReadOnlyList<CustomOrder> custom)
    {
        var output = new List<Candidate>();
        void Add(string id, string name, double order, string formula, string basis, string category, string key)
        {
            output.Add(new Candidate(id, name, order, formula, basis, category, key, !motor.HiddenCandidates.Contains(id)));
        }
        Add("rotation", "机械转频", 1, "fᵣ = n / 60", "机械轴每转一次的运动学参考；不等同于不平衡已被证实。", "运动学", "speed");
        if (motor.Topology == MotorTopology.BrushlessPermanentMagnet)
        {
            Add("electrical", "电气基频", motor.PolePairs, "fₑ = p × n / 60", "仅适用于同步运行的永磁无刷拓扑，p 为极对数；不是电磁力阶次。", "电气参考", "rotor");
            if (motor.IncludeSlotPass)
            {
                Add("slot", "槽通过参考", motor.Slots, "fₛ = Q × n / 60", "转子参考点每转经过 Q 个等间距定子槽；只表示相对运动周期，不指认声压主峰。", "运动学", "slots");
            }
            if (motor.IncludeIdealCogging && motor.SymmetricUnskewedAssumption && !string.IsNullOrWhiteSpace(motor.AssumptionNotes))
            {
                var order = LeastCommonMultiple(motor.Slots, 2 * motor.PolePairs);
                Add("cogging", "理想齿槽周期候选", order, "f𝚌 = LCM(Q, 2p) × n / 60", "等槽、对称永磁结构的理想周期；不含制造偏差、斜槽、饱和与负载影响。用户依据：" + motor.AssumptionNotes, "假设候选", "slots");
            }
        }
        else
        {
            Add("commutator", "单刷换向片通过参考", motor.CommutatorSegments, "fₖ = K × n / 60", "理想等宽 K 片换向器相对单个固定电刷的片通过；全机脉冲可能重合或出现不同谐波，不乘电刷数。", "运动学", "brush");
        }
        foreach (var order in custom.Where(x => x.Enabled))
        {
            Add("custom:" + order.Id, order.Name, order.Order, "f = k × n / 60", order.Basis, "用户候选", "custom");
        }
        return output;
    }

    public static double At(double order, double rpm)
    {
        if (!double.IsFinite(order) || !double.IsFinite(rpm) || order <= 0 || rpm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "阶次必须大于零、转速非负且均为有限数值。");
        }
        var frequency = order / 60.0 * rpm;
        if (!double.IsFinite(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(order), "换算结果超出有限数值范围。");
        }
        return frequency;
    }

    public static int GreatestCommonDivisor(int a, int b)
    {
        if (a <= 0 || b <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(a));
        }
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }

    public static int LeastCommonMultiple(int a, int b)
    {
        return checked(a / GreatestCommonDivisor(a, b) * b);
    }

    public static IReadOnlyList<double> Speeds(MotorConfiguration motor)
    {
        var errors = ProjectValidator.ValidateMotor(motor);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join("; ", errors.Select(x => x.Message)));
        }
        var values = new List<double>();
        var count = (int)Math.Floor((motor.MaximumRpm - motor.MinimumRpm) / motor.StepRpm);
        for (var index = 0; index <= count; index++)
        {
            values.Add(motor.MinimumRpm + index * motor.StepRpm);
        }
        if (Math.Abs(values[^1] - motor.MaximumRpm) > 1e-7)
        {
            values.Add(motor.MaximumRpm);
        }
        return values;
    }

    public static IReadOnlyList<FrequencyRow> Rows(Project project, bool fullScan)
    {
        var speeds = fullScan ? Speeds(project.Motor) : [project.Motor.InspectRpm];
        var candidates = Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible);
        var result = new List<FrequencyRow>();
        foreach (var candidate in candidates)
        {
            foreach (var rpm in speeds)
            {
                result.Add(new(candidate.Id, candidate.Name, candidate.Order, rpm, At(candidate.Order, rpm), candidate.Formula, candidate.Basis));
            }
        }
        return result;
    }

    public static IReadOnlyList<BandCrossing> Crossings(Project project)
    {
        var result = new List<BandCrossing>();
        foreach (var candidate in Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible))
        {
            foreach (var band in project.Bands.Where(x => x.Enabled))
            {
                var low = Math.Max(project.Motor.MinimumRpm, 60 * band.LowerHz / candidate.Order);
                var high = Math.Min(project.Motor.MaximumRpm, 60 * band.UpperHz / candidate.Order);
                if (low <= high)
                {
                    result.Add(new(candidate.Id, candidate.Name, band.Name, band.Id, band.Origin, low, high, band.LowerHz, band.UpperHz, band.Basis));
                }
            }
        }
        return result.OrderBy(x => x.LowerRpm).ToList();
    }

    public static IReadOnlyList<PeakMatch> MatchPeaks(Project project, Spectrum spectrum)
    {
        var candidates = Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible).ToList();
        var result = new List<PeakMatch>();
        var points = spectrum.Points;
        var peaks = Enumerable.Range(1, Math.Max(0, points.Count - 2))
            .Where(i => points[i].Amplitude > points[i - 1].Amplitude && points[i].Amplitude >= points[i + 1].Amplitude)
            .Select(i => points[i])
            .OrderByDescending(x => x.Amplitude)
            .Take(project.Review.MaximumPeaks);
        foreach (var peak in peaks)
        {
            var nearby = candidates
                .Select(x => (Candidate: x, Frequency: At(x.Order, spectrum.Rpm)))
                .Where(x => Math.Abs(x.Frequency - peak.FrequencyHz) <= project.Review.MatchToleranceHz)
                .OrderBy(x => Math.Abs(x.Frequency - peak.FrequencyHz))
                .ToList();
            if (nearby.Count == 0)
            {
                result.Add(new(spectrum.Name, spectrum.Rpm, peak.FrequencyHz, peak.Amplitude, spectrum.Unit, "无邻近候选", null, null, "容差内无候选；不排除未建模激励。"));
            }
            foreach (var item in nearby)
            {
                result.Add(new(spectrum.Name, spectrum.Rpm, peak.FrequencyHz, peak.Amplitude, spectrum.Unit, item.Candidate.Name, item.Frequency, peak.FrequencyHz - item.Frequency, "仅频率邻近，不能证明来源；同频候选均保留。"));
            }
        }
        return result;
    }

    public static IReadOnlyList<ComparisonRow> Compare(Project project, Scenario snapshot)
    {
        var current = Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible).ToDictionary(x => x.Id);
        var previous = Candidates(snapshot.Motor, snapshot.CustomOrders).Where(x => x.Visible).ToDictionary(x => x.Id);
        var result = new List<ComparisonRow>();
        foreach (var id in current.Keys.Union(previous.Keys))
        {
            current.TryGetValue(id, out var now);
            previous.TryGetValue(id, out var before);
            double? currentHz = now is null ? null : At(now.Order, project.Motor.InspectRpm);
            double? previousHz = before is null ? null : At(before.Order, project.Motor.InspectRpm);
            var statement = now is null ? "当前未启用" : before is null ? "快照未启用" : "统一按当前观察转速换算；差值=当前−快照";
            if (project.Motor.Topology != snapshot.Motor.Topology)
            {
                statement += "；拓扑不同，只作频率参考对照";
            }
            result.Add(new(now?.Name ?? before!.Name, now?.Order, before?.Order, currentHz, previousHz, currentHz - previousHz, statement));
        }
        return result;
    }

    public static IReadOnlyList<string> QualityNotes(Project project)
    {
        var notes = new List<string>();
        if (project.Demonstration)
        {
            notes.Add("本项目为模拟演示，不能证明实际车辆噪声来源或改善效果。");
        }
        foreach (var spectrum in project.Spectra)
        {
            if (spectrum.Rpm < project.Motor.MinimumRpm || spectrum.Rpm > project.Motor.MaximumRpm)
            {
                notes.Add(spectrum.Name + "：频谱转速在当前扫描范围外，Campbell 图不显示该切片。");
            }
            if (spectrum.Origin == DataOrigin.Simulated)
            {
                notes.Add(spectrum.Name + "：模拟频谱，保留来源标记。");
            }
            if (spectrum.Points.Count > 1 && spectrum.Points.Zip(spectrum.Points.Skip(1)).Any(x => x.Second.FrequencyHz - x.First.FrequencyHz > project.Review.MatchToleranceHz * 2))
            {
                notes.Add(spectrum.Name + "：部分频率间距大于两倍邻近容差，粗分辨率可能漏检；未插值补点。");
            }
        }
        if (project.Bands.Any(x => x.Enabled && x.UpperHz > project.Motor.MaximumHz))
        {
            notes.Add("部分敏感频段超出图表上限，交叠表仍按完整范围计算。");
        }
        var duplicates = Candidates(project.Motor, project.CustomOrders).Where(x => x.Visible).GroupBy(x => x.Order).Where(x => x.Count() > 1);
        foreach (var group in duplicates)
        {
            notes.Add($"{Labels.Number(group.Key)} 阶存在多个候选：{string.Join("、", group.Select(x => x.Name))}；图线可能重合，依据分别保留。");
        }
        notes.Add(Software.Boundary);
        return notes;
    }
}
