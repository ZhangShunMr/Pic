namespace Cabin.Core.Domain;

public sealed record Issue(string Path, string Message);

public static class Validation
{
    public static List<Issue> Check(Project project, bool forCalculation = true)
    {
        var issues = new List<Issue>();
        if (project.Schema != Software.Schema)
            issues.Add(new("schema", "只支持项目格式版本1"));
        if (project.Id == Guid.Empty)
            issues.Add(new("id", "项目标识不能为空"));
        Text(project.Name, "项目名称", 80, true, issues);
        Text(project.Revision, "修订号", 30, true, issues);
        Text(project.Owner, "负责人", 80, false, issues);
        Text(project.DataSource, "数据来源", 300, true, issues);
        Text(project.Notes, "备注", 3000, false, issues);
        if (project.Model is null || project.Sources is null || project.Seats is null ||
            project.Baselines is null || project.Reviews is null || project.Journal is null)
        {
            issues.Add(new("项目结构", "模型和集合不能为空"));
            return issues;
        }
        Model(project.Model, issues);
        Points(project.Model, project.Sources, project.Seats, issues);
        if (forCalculation)
        {
            if (project.Sources.All(x => !x.Enabled || x.Amplitude == 0))
                issues.Add(new("声源", "至少需要一个启用且幅值大于零的声源"));
            if (project.Seats.Count == 0)
                issues.Add(new("耳点", "至少添加一个座位耳点"));
        }
        if (project.Baselines.Count > 12)
            issues.Add(new("基准", "最多保留12个基准"));
        var baselineIds = new HashSet<Guid>();
        foreach (var baseline in project.Baselines)
        {
            if (baseline is null)
            {
                issues.Add(new("基准", "基准内容不能为null"));
                continue;
            }
            if (baseline.Id == Guid.Empty || !baselineIds.Add(baseline.Id))
                issues.Add(new("基准标识", "基准标识为空或重复"));
            Text(baseline.Name, "基准名称", 80, true, issues);
            Text(baseline.DataSource, "基准来源", 300, true, issues);
            if (baseline.Model is null || baseline.Sources is null || baseline.Seats is null)
            {
                issues.Add(new("基准", "基准模型与集合不能为空"));
                continue;
            }
            Model(baseline.Model, issues);
            Points(baseline.Model, baseline.Sources, baseline.Seats, issues);
        }
        if (project.Reviews.Count > 100)
            issues.Add(new("复核清单", "最多100项"));
        foreach (var item in project.Reviews)
        {
            if (item is null)
            {
                issues.Add(new("复核清单", "清单项不能为null"));
                continue;
            }
            Text(item.Target, "复核对象", 200, true, issues);
            Text(item.Action, "复核步骤", 1000, true, issues);
            Text(item.Owner, "复核负责人", 80, false, issues);
            Text(item.Result, "复核结果", 1000, true, issues);
        }
        if (project.Journal.Count > 500)
            issues.Add(new("历史", "最多500条历史记录"));
        foreach (var entry in project.Journal)
        {
            if (entry is null)
            {
                issues.Add(new("历史", "历史项不能为null"));
                continue;
            }
            Text(entry.Action, "历史操作", 100, true, issues);
            Text(entry.Detail, "历史详情", 1000, false, issues);
        }
        return issues;
    }

    public static void Model(Cavity model, List<Issue> issues)
    {
        Range(model.Length, 0.5, 10, "长度 Lx / m", issues);
        Range(model.Width, 0.5, 10, "宽度 Ly / m", issues);
        Range(model.Height, 0.5, 10, "高度 Lz / m", issues);
        Range(model.SoundSpeed, 300, 380, "声速 / m/s", issues);
        Range(model.Damping, 0.002, 0.2, "模态阻尼比", issues);
        Range(model.MinimumFrequency, 5, 499, "频带起点 / Hz", issues);
        Range(model.MaximumFrequency, 6, 500, "频带终点 / Hz", issues);
        Range(model.FrequencyStep, 0.25, 10, "频率步长 / Hz", issues);
        Range(model.Cutoff, 100, 1000, "模态截断 / Hz", issues);
        if (model.MinimumFrequency >= model.MaximumFrequency)
            issues.Add(new("频带", "终点必须大于起点"));
        if (model.Cutoff < model.MaximumFrequency)
            issues.Add(new("模态截断", "截断频率不能低于分析终点"));
        if ((model.MaximumFrequency - model.MinimumFrequency) / model.FrequencyStep > 2000)
            issues.Add(new("频率步长", "频率采样最多2001点"));
        if (double.IsFinite(model.Cutoff) && double.IsFinite(model.SoundSpeed) && model.SoundSpeed > 0)
        {
            var estimate = 4 * Math.PI / 3 * model.Volume * Math.Pow(model.Cutoff / model.SoundSpeed, 3);
            if (estimate > 6000)
                issues.Add(new("模态截断", "模型模态数量过大，请降低截断频率或缩小尺寸"));
        }
    }

    public static void Points(Cavity model, List<Emitter> sources, List<EarPoint> seats, List<Issue> issues)
    {
        if (sources.Count > 8)
            issues.Add(new("声源", "最多8个声源"));
        if (seats.Count > 12)
            issues.Add(new("耳点", "最多12个座位耳点"));
        var ids = new HashSet<Guid>();
        foreach (var source in sources)
        {
            if (source is null)
            {
                issues.Add(new("声源", "声源不能为null"));
                continue;
            }
            Identity(source.Id, ids, "声源", issues);
            Text(source.Name, "声源名称", 50, true, issues);
            Point(source.Point, model, source.Name, issues);
            Range(source.Amplitude, 0, 10, source.Name + "相对幅值", issues);
            Range(source.PhaseDegrees, -180, 180, source.Name + "相位 / °", issues);
        }
        foreach (var seat in seats)
        {
            if (seat is null)
            {
                issues.Add(new("耳点", "耳点不能为null"));
                continue;
            }
            Identity(seat.Id, ids, "耳点", issues);
            Text(seat.Name, "耳点名称", 50, true, issues);
            Text(seat.Note, "耳点备注", 300, false, issues);
            Point(seat.Point, model, seat.Name, issues);
        }
    }

    private static void Identity(Guid id, HashSet<Guid> ids, string path, List<Issue> issues)
    {
        if (id == Guid.Empty || !ids.Add(id))
            issues.Add(new(path, "标识为空或重复"));
    }

    private static void Point(Position? point, Cavity model, string path, List<Issue> issues)
    {
        if (point is null)
        {
            issues.Add(new(path, "坐标不能为空"));
            return;
        }
        Range(point.X, 0, model.Length, path + " x / m", issues);
        Range(point.Y, 0, model.Width, path + " y / m", issues);
        Range(point.Z, 0, model.Height, path + " z / m", issues);
    }

    private static void Text(string? value, string path, int maximum, bool required, List<Issue> issues)
    {
        if (value is null || (required && string.IsNullOrWhiteSpace(value)))
            issues.Add(new(path, "此字段不能为空"));
        else if (value.Length > maximum || value.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
            issues.Add(new(path, $"最多{maximum}字符，不允许控制字符"));
    }

    private static void Range(double value, double minimum, double maximum, string path, List<Issue> issues)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            issues.Add(new(path, $"必须为{minimum}至{maximum}之间的有限数值"));
    }
}
