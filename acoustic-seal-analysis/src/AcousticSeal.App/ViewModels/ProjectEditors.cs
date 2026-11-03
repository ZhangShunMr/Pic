using System.Globalization;
using System.Text.RegularExpressions;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.App.ViewModels;

public sealed class MetadataEditor : EditorViewModel
{
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public string VehicleProgramme { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Description { get; set; } = "";

    public MetadataEditor(AcousticProject project)
    {
        Id = project.Id;
        IsExisting = true;
        Name = project.Name;
        Owner = project.Owner;
        VehicleProgramme = project.VehicleProgramme;
        Objective = project.Objective;
        Description = project.Description;
    }

    public void ApplyTo(AcousticProject project)
    {
        project.Name = Text(Name);
        project.Owner = Text(Owner);
        project.VehicleProgramme = Text(VehicleProgramme);
        project.Objective = Text(Objective);
        project.Description = Text(Description);
    }
}

public sealed class LocationEditor : EditorViewModel
{
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Material { get; set; } = "";
    public string Extent { get; set; } = "";
    public string Procedure { get; set; } = "";
    public string RecoveryProcedure { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";

    public LocationEditor(SealLocation? item = null)
    {
        if (item is null)
        {
            return;
        }

        Id = item.Id;
        IsExisting = true;
        Name = item.Name;
        Zone = item.Zone;
        Material = item.Material;
        Extent = item.Extent;
        Procedure = item.Procedure;
        RecoveryProcedure = item.RecoveryProcedure;
        Reference = item.Reference;
        Notes = item.Notes;
    }

    public SealLocation ToModel()
    {
        return new SealLocation
        {
            Id = Id,
            Name = Text(Name),
            Zone = Text(Zone),
            Material = Text(Material),
            Extent = Text(Extent),
            Procedure = Text(Procedure),
            RecoveryProcedure = Text(RecoveryProcedure),
            Reference = Text(Reference),
            Notes = Text(Notes)
        };
    }
}

public sealed class ConditionEditor : EditorViewModel
{
    public string Name { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Microphone { get; set; } = "";
    public string Excitation { get; set; } = "";
    public string Surface { get; set; } = "";
    public string Gear { get; set; } = "";
    public string Weighting { get; set; } = "Z";
    public string TimeDefinition { get; set; } = "Leq";
    public string BandType { get; set; } = "octave";
    public string Instrument { get; set; } = "";
    public string Calibration { get; set; } = "";
    public string Unit { get; set; } = Software.LevelUnit;
    public string Speed { get; set; } = "";
    public string Rpm { get; set; } = "";
    public string Temperature { get; set; } = "";
    public string Duration { get; set; } = "";
    public string SampleRate { get; set; } = "";
    public string Synchronization { get; set; } = "";
    public bool NonOverlappingBands { get; set; }
    public string Notes { get; set; } = "";

    public ConditionEditor(MeasurementCondition? item = null)
    {
        if (item is null)
        {
            return;
        }

        Id = item.Id;
        IsExisting = true;
        Name = item.Name;
        Vehicle = item.Vehicle;
        Microphone = item.Microphone;
        Excitation = item.Excitation;
        Surface = item.Surface;
        Gear = item.Gear;
        Weighting = item.Weighting;
        TimeDefinition = item.TimeDefinition;
        BandType = item.BandType;
        Instrument = item.Instrument;
        Calibration = item.Calibration;
        Unit = item.Unit;
        Speed = InputNumber.Format(item.Speed);
        Rpm = InputNumber.Format(item.Rpm);
        Temperature = InputNumber.Format(item.Temperature);
        Duration = InputNumber.Format(item.Duration);
        SampleRate = InputNumber.Format(item.SampleRate);
        Synchronization = item.Synchronization;
        NonOverlappingBands = item.NonOverlappingBands;
        Notes = item.Notes;
    }

    public MeasurementCondition ToModel()
    {
        return new MeasurementCondition
        {
            Id = Id,
            Name = Text(Name),
            Vehicle = Text(Vehicle),
            Microphone = Text(Microphone),
            Excitation = Text(Excitation),
            Surface = Text(Surface),
            Gear = Text(Gear),
            Weighting = Weighting,
            TimeDefinition = Text(TimeDefinition),
            BandType = BandType,
            Instrument = Text(Instrument),
            Calibration = Text(Calibration),
            Unit = Unit,
            Speed = InputNumber.Optional(Speed, "车速", 0, 500),
            Rpm = InputNumber.Optional(Rpm, "转速", 0, 30000),
            Temperature = InputNumber.Optional(Temperature, "温度", -80, 100),
            Duration = InputNumber.Optional(Duration, "积分时长", 0.001, 86400),
            SampleRate = InputNumber.Optional(SampleRate, "采样率", 1, 1000000),
            Synchronization = Text(Synchronization),
            NonOverlappingBands = NonOverlappingBands,
            Notes = Text(Notes)
        };
    }
}

public sealed class BandRowEditor
{
    public Guid RowId { get; } = Guid.NewGuid();
    public string Frequency { get; set; } = "";
    public string Level { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Selected { get; set; }
}

public sealed class TrialEditor : EditorViewModel
{
    public string Name { get; set; } = "";
    public TrialKind Kind { get; set; }
    public Guid ConditionId { get; set; }
    public HashSet<Guid> LocationIds { get; } = [];
    public string Timestamp { get; set; } = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
    public string RepeatGroup { get; set; } = "";
    public SourceKind Source { get; set; } = SourceKind.Manual;
    public string SourceNote { get; set; } = "";
    public string OriginalFile { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Preparation { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<BandRowEditor> Bands { get; } = [];
    public string PasteText { get; set; } = "";

    public TrialEditor(AcousticTrial? item = null, Guid? defaultCondition = null)
    {
        ConditionId = defaultCondition ?? Guid.Empty;
        if (item is null)
        {
            AddBand(markDirty: false);
            return;
        }

        Id = item.Id;
        IsExisting = true;
        Name = item.Name;
        Kind = item.Kind;
        ConditionId = item.ConditionId;
        LocationIds.UnionWith(item.LocationIds);
        Timestamp = item.Timestamp.ToString("O", CultureInfo.InvariantCulture);
        RepeatGroup = item.RepeatGroup;
        Source = item.Source;
        SourceNote = item.SourceNote;
        OriginalFile = item.OriginalFile;
        Operator = item.Operator;
        Preparation = item.Preparation;
        Notes = item.Notes;
        Bands.AddRange(item.Bands.OrderBy(band => band.Frequency).Select(band => new BandRowEditor
        {
            Frequency = InputNumber.Format(band.Frequency),
            Level = InputNumber.Format(band.Level),
            Note = band.Note
        }));
    }

    public void ToggleLocation(Guid id, bool selected)
    {
        if (selected)
        {
            LocationIds.Add(id);
        }
        else
        {
            LocationIds.Remove(id);
        }

        MarkDirty();
    }

    public void AddBand(bool markDirty = true)
    {
        if (Bands.Count >= Software.MaximumBands)
        {
            throw new DomainValidationException("bands", "每试验最多200频带");
        }

        Bands.Add(new BandRowEditor());
        if (markDirty)
        {
            MarkDirty();
        }
    }

    public void RemoveBand(Guid id)
    {
        Bands.RemoveAll(row => row.RowId == id);
        MarkDirty();
    }

    public void RemoveSelectedBands()
    {
        Bands.RemoveAll(row => row.Selected);
        MarkDirty();
    }

    public void PasteBands()
    {
        var rows = CsvParser.Read(PasteText, ',');
        var parsed = new List<BandRowEditor>();
        foreach (var row in rows)
        {
            if (row.Cells.Count != 2)
            {
                throw new DomainValidationException("粘贴第" + row.Line + "行", "须为Hz,声级两列，不带表头");
            }

            var frequency = InputNumber.Required(row.Cells[0], "粘贴频率第" + row.Line + "行", 1, 100000);
            var value = row.Cells[1].Trim();
            var level = value is "" or "NA" or "N/A" or "null" ? null : InputNumber.Optional(value, "粘贴声级第" + row.Line + "行", -100, 200);
            parsed.Add(new BandRowEditor { Frequency = InputNumber.Format(frequency), Level = InputNumber.Format(level) });
        }

        if (parsed.Count == 0 || parsed.Count > Software.MaximumBands ||
            parsed.GroupBy(row => row.Frequency).Any(group => group.Count() > 1))
        {
            throw new DomainValidationException("paste", "频带为空、重复或超过200项，原表格未改变");
        }

        Bands.Clear();
        Bands.AddRange(parsed);
        MarkDirty();
    }

    public AcousticTrial ToModel()
    {
        if (!Regex.IsMatch(Timestamp.Trim(), "(Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            throw new DomainValidationException("timestamp", "请输入带时区ISO日期，例如2026-01-01T10:00:00+08:00");
        }

        var bands = new List<BandMeasurement>();
        for (var index = 0; index < Bands.Count; index++)
        {
            var row = Bands[index];
            if (string.IsNullOrWhiteSpace(row.Frequency) && string.IsNullOrWhiteSpace(row.Level) && string.IsNullOrWhiteSpace(row.Note))
            {
                continue;
            }

            bands.Add(new BandMeasurement
            {
                Frequency = InputNumber.Required(row.Frequency, "频带第" + (index + 1) + "行频率", 1, 100000),
                Level = InputNumber.Optional(row.Level, "频带第" + (index + 1) + "行声级", -100, 200),
                Note = Text(row.Note)
            });
        }

        return new AcousticTrial
        {
            Id = Id,
            Name = Text(Name),
            Kind = Kind,
            ConditionId = ConditionId,
            LocationIds = LocationIds.ToList(),
            Timestamp = timestamp,
            RepeatGroup = Text(RepeatGroup),
            Source = Source,
            SourceNote = Text(SourceNote),
            OriginalFile = Text(OriginalFile),
            Operator = Text(Operator),
            Preparation = Text(Preparation),
            Notes = Text(Notes),
            Bands = bands.OrderBy(band => band.Frequency).ToList()
        };
    }
}

public static class InputNumber
{
    public static double? Optional(string value, string path, double minimum, double maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, path, minimum, maximum);
    }

    public static double Required(string value, string path, double minimum, double maximum)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) || number < minimum || number > maximum)
        {
            throw new DomainValidationException(path, $"请输入{minimum}至{maximum}的有限数值，使用句点小数");
        }

        return number;
    }

    public static string Format(double? value)
    {
        return value?.ToString("G15", CultureInfo.InvariantCulture) ?? "";
    }
}
