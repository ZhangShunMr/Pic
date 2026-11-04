namespace TrimContact.Core.Domain;

public sealed record ValidationIssue(string Path, string Message);

public static class ProjectValidation
{
    public static IReadOnlyList<ValidationIssue> Check(DesignProject p, bool forCalculation = false)
    {
        var issues = new List<ValidationIssue>();
        void Text(string? value, string path, int length, bool required = false)
        {
            if (value is null || value.Length > length || (required && string.IsNullOrWhiteSpace(value)))
                issues.Add(new(path, $"应为{(required ? "非空" : "可空")}文本，最长{length}字"));
        }
        void Number(double value, string path, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                issues.Add(new(path, $"须为{min}至{max}之间的有限数值"));
        }
        void Count(int value, string path, int max)
        {
            if (value > max)
                issues.Add(new(path, $"最多{max}项"));
        }
        var ids = new HashSet<Guid>();
        void Id(Guid id, string path)
        {
            if (id == Guid.Empty || !ids.Add(id))
                issues.Add(new(path, "ID为空或重复"));
        }
        if (p.Schema != Product.Schema)
            issues.Add(new("格式版本", "仅支持schema=1，请使用匹配版本"));
        Id(p.Id, "项目ID");
        Text(p.Name, "项目名称", 100, true);
        Text(p.Revision, "项目修订", 30, true);
        Text(p.Owner, "负责人", 100);
        Text(p.Goal, "目标", 2000);
        Text(p.Source, "数据来源", 2000, true);
        if (!Enum.IsDefined(p.Origin))
            issues.Add(new("来源类型", "来源类型无效"));
        if (p.Panel is null || p.Settings is null || p.Variants is null || p.Loads is null || p.Tasks is null || p.Audit is null)
        {
            issues.Add(new("项目结构", "必需对象或列表缺失"));
            return issues;
        }
        var panel = p.Panel;
        Text(panel.Name, "饰板名称", 100, true);
        Number(panel.MassKg, "质量kg", 0.001, 100);
        Number(panel.CenterXmm, "质心Xmm", -10000, 10000);
        Number(panel.CenterYmm, "质心Ymm", -10000, 10000);
        if (panel.Outline is null)
        {
            issues.Add(new("轮廓", "轮廓列表缺失"));
            return issues;
        }
        Count(panel.Outline.Count, "轮廓顶点", 24);
        foreach (var vertex in panel.Outline)
        {
            if (vertex is null)
            {
                issues.Add(new("轮廓", "顶点不能为null"));
                continue;
            }
            Number(vertex.Xmm, "顶点Xmm", -10000, 10000);
            Number(vertex.Ymm, "顶点Ymm", -10000, 10000);
        }
        var outlineReady = panel.Outline.Count >= 3 && panel.Outline.All(v => v is not null);
        if (outlineReady && !Geometry.IsSimple(panel.Outline))
            issues.Add(new("轮廓", "轮廓须为无自交、无重复边的简单多边形，面积至少1 mm²"));
        if (forCalculation && !outlineReady)
            issues.Add(new("轮廓", "计算前至少定义3个顶点"));
        if (outlineReady && !Geometry.Contains(panel.Outline, panel.CenterXmm, panel.CenterYmm))
            issues.Add(new("质心", "须位于轮廓内或边界上"));
        if (p.Settings.Phases is not (24 or 48 or 72))
            issues.Add(new("相位采样", "仅支持24、48或72点"));
        Count(p.Variants.Count, "方案", 8);
        Count(p.Loads.Count, "载荷", 6);
        Count(p.Tasks.Count, "验证任务", 200);
        Count(p.Audit.Count, "操作记录", 500);
        if (forCalculation && (p.Variants.Count == 0 || p.Loads.Count == 0))
            issues.Add(new("计算条件", "至少建立一个方案和一个载荷"));
        void Position(double x, double y, string path)
        {
            Number(x, path + "/Xmm", -10000, 10000);
            Number(y, path + "/Ymm", -10000, 10000);
            if (outlineReady && !Geometry.Contains(panel.Outline, x, y))
                issues.Add(new(path, "点位须位于饰板轮廓内或边界上"));
        }
        foreach (var v in p.Variants)
        {
            if (v is null || v.Clips is null || v.Contacts is null)
            {
                issues.Add(new("方案", "方案或点位列表缺失"));
                continue;
            }
            Id(v.Id, "方案ID");
            Text(v.Name, "方案名称", 100, true);
            Text(v.Note, v.Name + "/说明", 2000);
            Count(v.Clips.Count, v.Name + "/卡扣", 12);
            Count(v.Contacts.Count, v.Name + "/接触点", 6);
            if (forCalculation && v.Clips.All(c => c is not null) && (v.Clips.Count < 3 || Geometry.TriangleSpread(v.Clips,
                Math.Max(1, Geometry.BoundsOf(panel.Outline).Scale)) < 1e-4))
                issues.Add(new(v.Name, "至少3个非共线卡扣，且归一化三角形面积不得过小"));
            foreach (var c in v.Clips)
            {
                if (c is null)
                {
                    issues.Add(new(v.Name, "卡扣不能为null"));
                    continue;
                }
                Id(c.Id, "卡扣ID");
                Text(c.Name, "卡扣名称", 100, true);
                Position(c.Xmm, c.Ymm, c.Name);
                Number(c.NormalStiffness, c.Name + "/法向刚度N/mm", 0.01, 100000);
                Number(c.TangentialStiffness, c.Name + "/切向刚度N/mm", 0.01, 100000);
                Number(c.ForceLimitN, c.Name + "/合力限值N", 0.01, 100000);
            }
            foreach (var c in v.Contacts)
            {
                if (c is null)
                {
                    issues.Add(new(v.Name, "接触点不能为null"));
                    continue;
                }
                Id(c.Id, "接触点ID");
                Text(c.Name, "接触点名称", 100, true);
                Text(c.Material, c.Name + "/材料", 100, true);
                Position(c.Xmm, c.Ymm, c.Name);
                Number(c.GapMm, c.Name + "/间隙mm", 0, 20);
                Number(c.ToleranceMm, c.Name + "/公差±mm", 0, 5);
                Number(c.PreloadMm, c.Name + "/预紧mm", 0, 20);
                Number(c.NormalStiffness, c.Name + "/法向刚度N/mm", 0.01, 100000);
                Number(c.TangentialStiffness, c.Name + "/切向刚度N/mm", 0.01, 100000);
                Number(c.Friction, c.Name + "/摩擦系数", 0, 2);
                Number(c.CompressionLimitMm, c.Name + "/压缩限值mm", 0.0001, 20);
            }
        }
        foreach (var l in p.Loads)
        {
            if (l is null)
            {
                issues.Add(new("载荷", "载荷不能为null"));
                continue;
            }
            Id(l.Id, "载荷ID");
            Text(l.Name, "载荷名称", 100, true);
            Text(l.Source, l.Name + "/来源", 2000, true);
            Number(l.NormalBiasG, l.Name + "/法向偏置g", -20, 20);
            Number(l.NormalAmplitudeG, l.Name + "/法向幅值g", 0, 20);
            Number(l.TangentialBiasG, l.Name + "/切向偏置g", -20, 20);
            Number(l.TangentialAmplitudeG, l.Name + "/切向幅值g", 0, 20);
            Number(l.PhaseDegrees, l.Name + "/相差°", -360, 360);
            Number(l.FrequencyHz, l.Name + "/频率标签Hz", 0, 1000);
        }
        var points = p.Variants.Where(v => v is not null && v.Clips is not null && v.Contacts is not null)
            .SelectMany(v => v.Clips.Where(c => c is not null).Select(c => c.Id)
                .Concat(v.Contacts.Where(c => c is not null).Select(c => c.Id))).ToHashSet();
        foreach (var t in p.Tasks)
        {
            if (t is null)
            {
                issues.Add(new("验证任务", "任务不能为null"));
                continue;
            }
            Id(t.Id, "任务ID");
            Text(t.Title, "任务标题", 200, true);
            Text(t.Method, t.Title + "/方法", 2000);
            Text(t.Acceptance, t.Title + "/判据", 2000);
            Text(t.Owner, t.Title + "/负责人", 100);
            Text(t.Evidence, t.Title + "/证据", 2000);
            if (t.PointId.HasValue && !points.Contains(t.PointId.Value))
                issues.Add(new(t.Title, "关联点位不存在"));
            if (t.Completed && string.IsNullOrWhiteSpace(t.Evidence))
                issues.Add(new(t.Title, "标记完成前须填写验证证据"));
        }
        foreach (var a in p.Audit)
            if (a is null || a.Action is null || a.Action.Length > 2000)
                issues.Add(new("操作记录", "记录缺失或超长"));
        return issues;
    }

    public static void Require(DesignProject p, bool forCalculation = false)
    {
        var issues = Check(p, forCalculation);
        if (issues.Count > 0)
            throw new InvalidDataException(string.Join("；", issues.Take(12).Select(i => i.Path + "：" + i.Message)));
    }
}
