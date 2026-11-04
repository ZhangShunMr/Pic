using System.Globalization;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Application;

public sealed class MotorDraft : ObservableObject
{
    private string name = "当前电机";
    private MotorTopology topology;
    private string polePairs = "2";
    private string slots = "12";
    private string segments = "10";
    private string minimum = "0";
    private string maximum = "6000";
    private string step = "100";
    private string inspect = "1800";
    private string maximumHz = "1500";
    private bool slotPass = true;
    private bool cogging;
    private bool symmetric;
    private string assumptions = "";

    public string Name { get => name; set => Set(ref name, value); }
    public MotorTopology Topology
    {
        get => topology;
        set
        {
            if (Set(ref topology, value))
            {
                Raise(nameof(IsBrushless));
                Raise(nameof(IsBrushed));
            }
        }
    }
    public bool IsBrushless => Topology == MotorTopology.BrushlessPermanentMagnet;
    public bool IsBrushed => Topology == MotorTopology.BrushedPermanentMagnet;
    public string PolePairs { get => polePairs; set => Set(ref polePairs, value); }
    public string Slots { get => slots; set => Set(ref slots, value); }
    public string Segments { get => segments; set => Set(ref segments, value); }
    public string MinimumRpm { get => minimum; set => Set(ref minimum, value); }
    public string MaximumRpm { get => maximum; set => Set(ref maximum, value); }
    public string StepRpm { get => step; set => Set(ref step, value); }
    public string InspectRpm { get => inspect; set => Set(ref inspect, value); }
    public string MaximumHz { get => maximumHz; set => Set(ref maximumHz, value); }
    public bool IncludeSlotPass { get => slotPass; set => Set(ref slotPass, value); }
    public bool IncludeCogging { get => cogging; set => Set(ref cogging, value); }
    public bool Symmetric { get => symmetric; set => Set(ref symmetric, value); }
    public string Assumptions { get => assumptions; set => Set(ref assumptions, value); }

    public static MotorDraft From(MotorConfiguration motor)
    {
        return new MotorDraft
        {
            Name = motor.Name,
            Topology = motor.Topology,
            PolePairs = motor.PolePairs.ToString(CultureInfo.InvariantCulture),
            Slots = motor.Slots.ToString(CultureInfo.InvariantCulture),
            Segments = motor.CommutatorSegments.ToString(CultureInfo.InvariantCulture),
            MinimumRpm = Labels.Number(motor.MinimumRpm),
            MaximumRpm = Labels.Number(motor.MaximumRpm),
            StepRpm = Labels.Number(motor.StepRpm),
            InspectRpm = Labels.Number(motor.InspectRpm),
            MaximumHz = Labels.Number(motor.MaximumHz),
            IncludeSlotPass = motor.IncludeSlotPass,
            IncludeCogging = motor.IncludeIdealCogging,
            Symmetric = motor.SymmetricUnskewedAssumption,
            Assumptions = motor.AssumptionNotes
        };
    }

    public MotorConfiguration Build(MotorConfiguration previous)
    {
        var motor = ProjectCodec.Clone(previous);
        motor.Name = Name.Trim();
        motor.Topology = Topology;
        motor.PolePairs = Integer(PolePairs, "极对数 p");
        motor.Slots = Integer(Slots, "定子槽数 Q");
        motor.CommutatorSegments = Integer(Segments, "换向片数 K");
        motor.MinimumRpm = Number(MinimumRpm, "扫描起始转速");
        motor.MaximumRpm = Number(MaximumRpm, "扫描终止转速");
        motor.StepRpm = Number(StepRpm, "扫描步长");
        motor.InspectRpm = Number(InspectRpm, "观察转速");
        motor.MaximumHz = Number(MaximumHz, "图表频率上限");
        motor.IncludeSlotPass = IncludeSlotPass;
        motor.IncludeIdealCogging = IncludeCogging;
        motor.SymmetricUnskewedAssumption = Symmetric;
        motor.AssumptionNotes = Assumptions.Trim();
        var issues = ProjectValidator.ValidateMotor(motor);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues.Select(x => x.Path + "：" + x.Message)));
        }
        return motor;
    }

    public bool DiffersFrom(MotorConfiguration applied)
    {
        var other = From(applied);
        return Name != other.Name || Topology != other.Topology
            || PolePairs != other.PolePairs || Slots != other.Slots || Segments != other.Segments
            || MinimumRpm != other.MinimumRpm || MaximumRpm != other.MaximumRpm
            || StepRpm != other.StepRpm || InspectRpm != other.InspectRpm
            || MaximumHz != other.MaximumHz || IncludeSlotPass != other.IncludeSlotPass
            || IncludeCogging != other.IncludeCogging || Symmetric != other.Symmetric
            || Assumptions != other.Assumptions;
    }

    public static double Number(string text, string field)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw new InvalidDataException(field + "：请输入有限数值，小数点使用英文句点，不能含千位逗号。");
        }
        return value;
    }

    public static int Integer(string text, string field)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException(field + "：请输入整数。");
        }
        return value;
    }
}
