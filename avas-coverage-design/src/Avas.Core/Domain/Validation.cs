namespace Avas.Core.Domain;

public sealed record Issue(string Path, string Message)
{
    public override string ToString() => $"{Path}：{Message}";
}

public static class Validation
{
    public static List<Issue> Check(Project project, bool forCalculation = false)
    {
        var issues = new List<Issue>();
        if (project.Schema != 1)
            issues.Add(new("Schema", "仅支持项目格式版本1，不能静默迁移"));
        Text(issues, "项目名称", project.Name, 100, true);
        Text(issues, "负责人", project.Owner, 100);
        Text(issues, "车辆", project.Vehicle, 100);
        Text(issues, "目的", project.Purpose, 2000);
        Text(issues, "备注", project.Note, 4000);
        if (project.Id == Guid.Empty)
            issues.Add(new("项目标识", "不能是空GUID"));
        if (project.Speakers is null || project.Listeners is null || project.Settings is null || project.Snapshots is null || project.Audit is null)
        {
            issues.Add(new("项目结构", "集合与设置不能为null"));
            return issues;
        }
        if (project.Speakers.Count > 8)
            issues.Add(new("扬声器", "最多8个"));
        if (project.Listeners.Count > 100)
            issues.Add(new("观察点", "最多100个"));
        if (project.Snapshots.Count > 20)
            issues.Add(new("方案快照", "最多20个"));
        if (project.Audit.Count > 1000)
            issues.Add(new("审计", "最多1000条"));
        Unique(issues, "扬声器", project.Speakers.Select(x => x.Id));
        Unique(issues, "观察点", project.Listeners.Select(x => x.Id));
        Unique(issues, "方案", project.Snapshots.Select(x => x.Id));
        foreach (var source in project.Speakers)
            CheckSpeaker(issues, source);
        foreach (var point in project.Listeners)
        {
            Text(issues, $"观察点 {point.Id} 名称", point.Name, 100, true);
            Range(issues, $"{point.Name}.X", point.X, -100, 100);
            Range(issues, $"{point.Name}.Y", point.Y, -100, 100);
            Text(issues, $"{point.Name}.备注", point.Note, 2000);
        }
        CheckSettings(issues, project.Settings);
        foreach (var snapshot in project.Snapshots)
        {
            Text(issues, "快照名称", snapshot.Name, 100, true);
            if (snapshot.Settings is null || snapshot.Speakers is null || snapshot.Listeners is null)
            {
                issues.Add(new("快照", "缺少设置、扬声器或观察点"));
                continue;
            }
            var nested = new Project
            {
                Name = snapshot.Name,
                Settings = snapshot.Settings,
                Speakers = snapshot.Speakers,
                Listeners = snapshot.Listeners
            };
            issues.AddRange(Check(nested).Select(x => new Issue("快照/" + x.Path, x.Message)));
        }
        if (forCalculation && !project.Speakers.Any(x => x.Enabled))
            issues.Add(new("扬声器", "请添加并启用至少一个扬声器"));
        return issues;
    }

    public static void CheckSpeaker(List<Issue> issues, Speaker speaker)
    {
        var prefix = $"扬声器 {speaker.Name}";
        Text(issues, prefix + ".名称", speaker.Name, 100, true);
        Text(issues, prefix + ".来源", speaker.Source, 2000, true);
        Text(issues, prefix + ".备注", speaker.Note, 2000);
        Range(issues, prefix + ".X", speaker.X, -100, 100);
        Range(issues, prefix + ".Y", speaker.Y, -100, 100);
        Range(issues, prefix + ".高度", speaker.Height, 0, 10);
        Range(issues, prefix + ".朝向", speaker.Heading, -360, 360);
        Range(issues, prefix + ".参考距离", speaker.ReferenceDistance, 0.1, 10);
        Range(issues, prefix + ".增益", speaker.Gain, -40, 40);
        Range(issues, prefix + ".指向指数", speaker.Exponent, 0, 8);
        Range(issues, prefix + ".背向幅值下限", speaker.RearFloor, 0.01, 1);
        Bands(issues, prefix + ".频带", speaker.Bands, true);
        if (!Enum.IsDefined(speaker.Origin))
            issues.Add(new(prefix + ".来源类别", "无效枚举"));
        Asset(issues, prefix + ".音频", speaker.Audio);
    }

    public static void CheckSettings(List<Issue> issues, Settings settings)
    {
        Range(issues, "网格.MinX", settings.MinX, -100, 100);
        Range(issues, "网格.MaxX", settings.MaxX, -100, 100);
        Range(issues, "网格.MinY", settings.MinY, -100, 100);
        Range(issues, "网格.MaxY", settings.MaxY, -100, 100);
        Range(issues, "网格步长", settings.Step, 0.25, 5);
        Range(issues, "接收高度", settings.ReceiverHeight, 0, 10);
        Range(issues, "距离下限", settings.MinimumDistance, 0.1, 10);
        Range(issues, "车辆长度", settings.VehicleLength, 1, 20);
        Range(issues, "车辆宽度", settings.VehicleWidth, 0.5, 5);
        Range(issues, "工程声级目标", settings.LevelTarget, -60, 140);
        Range(issues, "工程背景差目标", settings.MarginTarget, -40, 60);
        if (settings.MaxX <= settings.MinX || settings.MaxY <= settings.MinY)
            issues.Add(new("网格", "最大坐标必须大于最小坐标"));
        if (settings.Step > 0 && double.IsFinite(settings.Step))
        {
            var count = (Math.Floor((settings.MaxX - settings.MinX) / settings.Step) + 1)
                * (Math.Floor((settings.MaxY - settings.MinY) / settings.Step) + 1);
            if (!double.IsFinite(count) || count > 20000)
                issues.Add(new("网格", "节点上限20000，请增大步长或缩小范围"));
        }
        if (settings.SelectedHz != 0 && !Software.Frequencies.Contains(settings.SelectedHz))
            issues.Add(new("显示频带", "请选择0（合成）或七个支持频带"));
        Text(issues, "背景来源", settings.BackgroundSource, 2000, true);
        if (!Enum.IsDefined(settings.BackgroundOrigin))
            issues.Add(new("背景来源类别", "无效枚举"));
        Bands(issues, "背景频带", settings.Background, false);
        Asset(issues, "背景音频", settings.BackgroundAudio);
    }

    public static void Bands(List<Issue> issues, string path, List<BandInput>? bands, bool correction)
    {
        if (bands is null || bands.Count != Software.Frequencies.Length)
        {
            issues.Add(new(path, "必须完整提供七个频带，缺失不能补零"));
            return;
        }
        if (!bands.Select(x => x.Hz).Order().SequenceEqual(Software.Frequencies))
            issues.Add(new(path, "频率必须各出现一次：125,250,500,1000,2000,4000,8000"));
        foreach (var band in bands)
        {
            Range(issues, $"{path}.{band.Hz}Hz声级", band.Level, -60, 140);
            Range(issues, $"{path}.{band.Hz}Hz修正", band.Correction, correction ? -40 : 0, correction ? 40 : 0);
        }
    }

    private static void Asset(List<Issue> issues, string path, AudioAsset? asset)
    {
        if (asset is null)
            return;
        Text(issues, path + ".名称", asset.Name, 200, true);
        Text(issues, path + ".来源", asset.Source, 2000, true);
        if (asset.Data is null || asset.Data.Length > 14000000)
            issues.Add(new(path, "单音频最大10MiB"));
        Range(issues, path + ".时长", asset.Duration, 0.001, 30);
        if (asset.Rate is < 16000 or > 96000 || asset.Channels is < 1 or > 2)
            issues.Add(new(path, "采样率16000至96000Hz，单声道或双声道"));
    }

    private static void Unique(List<Issue> issues, string path, IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        if (list.Contains(Guid.Empty) || list.Distinct().Count() != list.Count)
            issues.Add(new(path, "对象标识为空或重复"));
    }

    private static void Text(List<Issue> issues, string path, string? value, int max, bool required = false)
    {
        if (value is null || value.Length > max || (required && string.IsNullOrWhiteSpace(value)))
            issues.Add(new(path, $"{(required ? "必填；" : "")}最多{max}字符"));
    }

    private static void Range(List<Issue> issues, string path, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            issues.Add(new(path, $"必须为{min}至{max}的有限数值"));
    }
}
