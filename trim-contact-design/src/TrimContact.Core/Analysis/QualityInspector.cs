using TrimContact.Core.Domain;

namespace TrimContact.Core.Analysis;

public sealed record QualityFinding(string Severity, string Target, string Finding, string Action);

public sealed record LayoutMetric(string Variant, string Point, string Kind,
    double NearestClipDistanceMm, double EffectiveInterferenceMm, double MinimumInterferenceMm,
    double MaximumInterferenceMm);

public static class QualityInspector
{
    public static IReadOnlyList<LayoutMetric> Metrics(DesignProject project)
    {
        var rows = new List<LayoutMetric>();
        foreach (var variant in project.Variants)
        {
            double Distance(double x, double y, Guid exclude)
            {
                return variant.Clips.Where(c => c.Id != exclude)
                    .Select(c => Math.Sqrt(Math.Pow(c.Xmm - x, 2) + Math.Pow(c.Ymm - y, 2)))
                    .DefaultIfEmpty(double.NaN).Min();
            }
            foreach (var clip in variant.Clips)
                rows.Add(new(variant.Name, clip.Name, "卡扣", Distance(clip.Xmm, clip.Ymm, clip.Id), 0, 0, 0));
            foreach (var contact in variant.Contacts)
                rows.Add(new(variant.Name, contact.Name, "接触点", Distance(contact.Xmm, contact.Ymm, Guid.Empty),
                    contact.PreloadMm - contact.GapMm, contact.PreloadMm - contact.GapMm - contact.ToleranceMm,
                    contact.PreloadMm - contact.GapMm + contact.ToleranceMm));
        }
        return rows;
    }

    public static IReadOnlyList<QualityFinding> Inspect(DesignProject project, AnalysisReport? report)
    {
        var rows = new List<QualityFinding>();
        foreach (var issue in ProjectValidation.Check(project, true))
            rows.Add(new("阻断", issue.Path, issue.Message, "在对应输入页修正，再应用并计算。"));
        if (project.Origin == DataOrigin.Simulation)
            rows.Add(new("来源", "整个项目", "当前为模拟演示参数，不能证明实车表现。", "完成参数识别与真实样件验证后，记录数据来源。"));
        if (project.Origin == DataOrigin.MeasurementDeclared)
            rows.Add(new("来源", "整个项目", "实测状态由用户声明，软件没有认证标定链。", "保存仪器、加载条件、温度与原始试验编号。"));
        if (project.Source.Contains("待") || project.Source.Contains("估算"))
            rows.Add(new("待核实", "参数来源", "来源含待确认或估算说明。", "逐项补充参数识别依据，不能仅改变来源类型。"));
        foreach (var load in project.Loads)
        {
            if (load.NormalAmplitudeG == 0 && load.TangentialAmplitudeG == 0)
                rows.Add(new("说明", load.Name, "零幅值载荷等效于静态偏置。", "若目标为周期工况，请确认输入的是幅值而不是峰峰值。"));
            if (load.FrequencyHz > 0)
                rows.Add(new("限制", load.Name, $"{load.FrequencyHz}Hz只用作工况标签，未进行动态放大。", "存在共振或冲击时，使用动力学模型与台架试验。"));
        }
        foreach (var variant in project.Variants)
        {
            var coordinates = variant.Clips.Select(c => (c.Xmm, c.Ymm, c.Name))
                .Concat(variant.Contacts.Select(c => (c.Xmm, c.Ymm, c.Name))).ToArray();
            for (int i = 0; i < coordinates.Length; i++)
                for (int j = i + 1; j < coordinates.Length; j++)
                    if (Math.Abs(coordinates[i].Xmm - coordinates[j].Xmm) < 1e-6
                        && Math.Abs(coordinates[i].Ymm - coordinates[j].Ymm) < 1e-6)
                        rows.Add(new("复核", variant.Name, coordinates[i].Name + "与" + coordinates[j].Name + "坐标重合。",
                            "并联等效可能是有意设计，请核查重复输入与空间布置。"));
            foreach (var c in variant.Contacts)
            {
                if (c.PreloadMm - c.GapMm - c.ToleranceMm <= 0 && c.PreloadMm - c.GapMm + c.ToleranceMm > 0)
                    rows.Add(new("敏感", variant.Name + "/" + c.Name, "未平衡的装配干涉范围跨过零。",
                        "启用全部间隙公差角点，再核查平衡后的最小压缩。"));
                if (c.Friction == 0)
                    rows.Add(new("说明", c.Name, "摩擦系数为零，不提供接触摩擦力。", "确认是否为有意的无摩擦筛查假设。"));
            }
        }
        if (report is null)
        {
            rows.Add(new("待计算", "结果", "尚无当前结果。", "应用输入后运行全部方案。"));
            return rows;
        }
        foreach (var variant in report.Variants)
        {
            var length = Geometry.BoundsOf(project.Panel.Outline).Scale;
            var states = variant.NominalTraces.SelectMany(t => t.Samples).ToArray();
            var maxRotation = states.Select(s => Math.Max(Math.Abs(s.Displacement.X), Math.Abs(s.Displacement.Y)) / length)
                .DefaultIfEmpty().Max();
            var maxTranslation = states.Select(s => Math.Max(Math.Abs(s.Displacement.Z), Math.Abs(s.TangentialMm)) / length)
                .DefaultIfEmpty().Max();
            if (maxRotation > 0.05 || maxTranslation > 0.05)
                rows.Add(new("适用性", variant.Name, "名义采样位移或等效转角超过几何尺度的5%工程提醒值。",
                    "复核小位移假设；该提醒阈值不是经过验证的材料限值，角点仍须另行复核。"));
            foreach (var c in variant.Contacts)
            {
                if (c.MinCompressionMm <= Product.ContactEpsilon)
                    rows.Add(new("分离", variant.Name + "/" + c.Name, c.State, "查看最小压缩控制工况并生成装配间隙验证任务。"));
                if (c.MaxSlipMm > Product.ContactEpsilon)
                    rows.Add(new("滑移", variant.Name + "/" + c.Name, $"滑移余量包络{c.MaxSlipMm:G5}mm。",
                        "核查材料副、切向刚度和摩擦试验，不以滑移直接判定有声音。"));
                if (c.MaxUtilization > 1)
                    rows.Add(new("超限", c.Name, "压缩量超过用户输入允许值。", "确认允许压缩量来源并检查控制工况。"));
            }
            foreach (var c in variant.Clips.Where(c => c.MaxUtilization > 1))
                rows.Add(new("超限", c.Name, "卡扣合力超过用户输入限值。", "核查保持力方向、刚度和供应商试验数据。"));
        }
        if (!report.ToleranceEnabled)
            rows.Add(new("限制", "公差", "本次结果只包含名义间隙。", "在载荷页启用公差角点后重新计算。"));
        return rows;
    }
}
