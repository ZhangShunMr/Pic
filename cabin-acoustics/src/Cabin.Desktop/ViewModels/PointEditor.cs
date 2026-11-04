using Cabin.Core.Domain;
using Cabin.Desktop.Infrastructure;

namespace Cabin.Desktop.ViewModels;

public sealed class PointEditor : ObservableObject
{
    private bool enabled;
    private readonly Action changed;

    public PointEditor(Emitter source, Action changed) : this(source.Id, source.Name, source.Point, changed)
    {
        IsSource = true;
        enabled = source.Enabled;
        Amplitude = InputField.Number("相对幅值", source.Amplitude, 0, 10, changed, "无量纲，相干源幅值");
        Phase = InputField.Number("相位 / °", source.PhaseDegrees, -180, 180, changed, "共用相位参考");
    }

    public PointEditor(EarPoint seat, Action changed) : this(seat.Id, seat.Name, seat.Point, changed)
    {
        Note.Text = seat.Note;
    }

    private PointEditor(Guid id, string name, Position point, Action changed)
    {
        Id = id;
        this.changed = changed;
        Name = new("名称", name, changed, required: true, maximumLength: 50);
        X = InputField.Number("x / m", point.X, 0, 10, changed, "沿座舱长度方向，原点为前左下角");
        Y = InputField.Number("y / m", point.Y, 0, 10, changed, "沿座舱宽度方向");
        Z = InputField.Number("z / m", point.Z, 0, 10, changed, "沿座舱高度方向");
        Amplitude = InputField.Number("相对幅值", 1, 0, 10, changed, "");
        Phase = InputField.Number("相位 / °", 0, -180, 180, changed, "");
        Note = new("备注", "", changed, maximumLength: 300);
    }

    public Guid Id { get; }
    public bool IsSource { get; }
    public InputField Name { get; }
    public InputField X { get; }
    public InputField Y { get; }
    public InputField Z { get; }
    public InputField Amplitude { get; }
    public InputField Phase { get; }
    public InputField Note { get; }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (Set(ref enabled, value))
                changed();
        }
    }

    public Emitter Source()
    {
        EnsureValid();
        return new()
        {
            Id = Id,
            Name = Name.Text,
            Point = Point(),
            Amplitude = Amplitude.Value,
            PhaseDegrees = Phase.Value,
            Enabled = Enabled
        };
    }

    public EarPoint Seat()
    {
        EnsureValid();
        return new() { Id = Id, Name = Name.Text, Point = Point(), Note = Note.Text };
    }

    public void EnsureValid()
    {
        foreach (var field in new[] { Name, X, Y, Z, Note })
            field.EnsureValid();
        if (IsSource)
        {
            Amplitude.EnsureValid();
            Phase.EnsureValid();
        }
    }

    private Position Point() => new(X.Value, Y.Value, Z.Value);
}
