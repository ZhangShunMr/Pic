using System.Globalization;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.App.ViewModels;

public sealed class MotorDraft
{
    public string Name { get; set; } = "当前电机";
    public MotorTopology Topology { get; set; }
    public string PolePairs { get; set; } = "2";
    public string Slots { get; set; } = "12";
    public string Segments { get; set; } = "10";
    public string MinimumRpm { get; set; } = "0";
    public string MaximumRpm { get; set; } = "6000";
    public string StepRpm { get; set; } = "100";
    public string InspectRpm { get; set; } = "1800";
    public string MaximumHz { get; set; } = "1500";
    public bool IncludeSlotPass { get; set; } = true;
    public bool IncludeCogging { get; set; }
    public bool Symmetric { get; set; }
    public string Assumptions { get; set; } = "";

    public static MotorDraft From(MotorConfiguration motor)
    {
        return new MotorDraft
        {
            Name = motor.Name,
            Topology = motor.Topology,
            PolePairs = Labels.Number(motor.PolePairs),
            Slots = Labels.Number(motor.Slots),
            Segments = Labels.Number(motor.CommutatorSegments),
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
        var result = ProjectCodec.Clone(previous);
        result.Name = Name.Trim();
        result.Topology = Topology;
        result.PolePairs = ParseInt(PolePairs, "极对数");
        result.Slots = ParseInt(Slots, "槽数");
        result.CommutatorSegments = ParseInt(Segments, "换向片数");
        result.MinimumRpm = Parse(MinimumRpm, "起始转速");
        result.MaximumRpm = Parse(MaximumRpm, "终止转速");
        result.StepRpm = Parse(StepRpm, "扫描步长");
        result.InspectRpm = Parse(InspectRpm, "观察转速");
        result.MaximumHz = Parse(MaximumHz, "频率上限");
        result.IncludeSlotPass = IncludeSlotPass;
        result.IncludeIdealCogging = IncludeCogging;
        result.SymmetricUnskewedAssumption = Symmetric;
        result.AssumptionNotes = Assumptions.Trim();
        return result;
    }

    public static double Parse(string value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            throw new InvalidDataException(name + "：请输入有限数值，小数点用英文句点。");
        }
        return number;
    }

    public static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidDataException(name + "：请输入整数。");
        }
        return number;
    }
}
