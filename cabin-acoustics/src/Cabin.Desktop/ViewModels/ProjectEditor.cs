using System.Collections.ObjectModel;
using Cabin.Core.Domain;
using Cabin.Core.IO;

namespace Cabin.Desktop.ViewModels;

public sealed class ProjectEditor
{
    private readonly Project original;
    private readonly Action changed;

    public ProjectEditor(Project project, Action changed)
    {
        original = ProjectCodec.Clone(project);
        this.changed = changed;
        Name = new("项目名称", project.Name, changed, required: true, maximumLength: 80);
        Revision = new("修订号", project.Revision, changed, required: true, maximumLength: 30);
        Owner = new("负责人", project.Owner, changed, maximumLength: 80);
        Source = new("数据来源", project.DataSource, changed, "请说明尺寸和点位的来源", true, 300);
        Notes = new("项目备注", project.Notes, changed);
        Metadata = new[] { Name, Revision, Owner, Source, Notes };
        var m = project.Model;
        Length = Number("长度 Lx / m", m.Length, 0.5, 10, "0.5—10m；x轴方向");
        Width = Number("宽度 Ly / m", m.Width, 0.5, 10, "0.5—10m；y轴方向");
        Height = Number("高度 Lz / m", m.Height, 0.5, 10, "0.5—10m；z轴方向");
        SoundSpeed = Number("声速 / m/s", m.SoundSpeed, 300, 380, "默认343，人工定义，不自动换算气温");
        Damping = Number("等效模态阻尼比", m.Damping, 0.002, 0.2, "默认0.03；全模态使用同一阻尼比");
        Minimum = Number("频带起点 / Hz", m.MinimumFrequency, 5, 499, "必须小于终点");
        Maximum = Number("频带终点 / Hz", m.MaximumFrequency, 6, 500, "必须不高于截断频率");
        Step = Number("频率步长 / Hz", m.FrequencyStep, 0.25, 10, "默认1；可能漏掉窄峰，需细化核对");
        Cutoff = Number("模态截断 / Hz", m.Cutoff, 100, 1000, "默认400；影响非零阶模态集合");
        Geometry = new[] { Length, Width, Height, SoundSpeed, Damping };
        Analysis = new[] { Minimum, Maximum, Step, Cutoff };
        Sources = new(project.Sources.Select(x => new PointEditor(x, changed)));
        Seats = new(project.Seats.Select(x => new PointEditor(x, changed)));
    }

    public bool IsDemo => original.IsDemo;
    public string SourceBadge => IsDemo ? "模拟研究 · 非实测" : "人工理想模型 · 未校准";
    public InputField Name { get; }
    public InputField Revision { get; }
    public InputField Owner { get; }
    public InputField Source { get; }
    public InputField Notes { get; }
    public InputField Length { get; }
    public InputField Width { get; }
    public InputField Height { get; }
    public InputField SoundSpeed { get; }
    public InputField Damping { get; }
    public InputField Minimum { get; }
    public InputField Maximum { get; }
    public InputField Step { get; }
    public InputField Cutoff { get; }
    public IReadOnlyList<InputField> Metadata { get; }
    public IReadOnlyList<InputField> Geometry { get; }
    public IReadOnlyList<InputField> Analysis { get; }
    public ObservableCollection<PointEditor> Sources { get; }
    public ObservableCollection<PointEditor> Seats { get; }

    public Project Build(bool forCalculation)
    {
        foreach (var field in Metadata.Concat(Geometry).Concat(Analysis))
            field.EnsureValid();
        var project = ProjectCodec.Clone(original);
        project.Name = Name.Text;
        project.Revision = Revision.Text;
        project.Owner = Owner.Text;
        project.DataSource = Source.Text;
        project.Notes = Notes.Text;
        project.Modified = DateTimeOffset.UtcNow;
        project.Model = new()
        {
            Length = Length.Value,
            Width = Width.Value,
            Height = Height.Value,
            SoundSpeed = SoundSpeed.Value,
            Damping = Damping.Value,
            MinimumFrequency = Minimum.Value,
            MaximumFrequency = Maximum.Value,
            FrequencyStep = Step.Value,
            Cutoff = Cutoff.Value
        };
        project.Sources = Sources.Select(x => x.Source()).ToList();
        project.Seats = Seats.Select(x => x.Seat()).ToList();
        var issues = Validation.Check(project, forCalculation);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, issues.Select(x => x.Path + "：" + x.Message)));
        return project;
    }

    public void AddSource()
    {
        if (Sources.Count >= 8)
            throw new ArgumentException("最多8个声源");
        Sources.Add(new(new Emitter { Name = "声源 " + (Sources.Count + 1), Point = new(0, 0, 0) }, changed));
        changed();
    }

    public void AddSeat()
    {
        if (Seats.Count >= 12)
            throw new ArgumentException("最多12个座位耳点");
        Seats.Add(new(new EarPoint { Name = "耳点 " + (Seats.Count + 1), Point = new(0, 0, 0) }, changed));
        changed();
    }

    private InputField Number(string name, double value, double min, double max, string hint)
    {
        return InputField.Number(name, value, min, max, changed, hint);
    }
}
