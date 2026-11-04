using MotorFrequency.Core.Domain;

namespace MotorFrequency.Application;

public sealed class ProjectDraft : ObservableObject
{
    private string name = "未命名项目";
    private string owner = "";
    private string purpose = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string Owner { get => owner; set => Set(ref owner, value); }
    public string Purpose { get => purpose; set => Set(ref purpose, value); }

    public static ProjectDraft From(Project project)
    {
        return new() { Name = project.Name, Owner = project.Owner, Purpose = project.Purpose };
    }

    public bool DiffersFrom(Project project)
    {
        return Name != project.Name || Owner != project.Owner || Purpose != project.Purpose;
    }
}

public sealed class OrderDraft : ObservableObject
{
    private string name = "";
    private string order = "2";
    private string basis = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string Order { get => order; set => Set(ref order, value); }
    public string Basis { get => basis; set => Set(ref basis, value); }
    public bool HasInput => Name.Length > 0 || Basis.Length > 0;
}

public sealed class BandDraft : ObservableObject
{
    private string name = "";
    private string lower = "55";
    private string upper = "65";
    private string basis = "";
    private DataOrigin origin;
    public string Name { get => name; set => Set(ref name, value); }
    public string Lower { get => lower; set => Set(ref lower, value); }
    public string Upper { get => upper; set => Set(ref upper, value); }
    public string Basis { get => basis; set => Set(ref basis, value); }
    public DataOrigin Origin { get => origin; set => Set(ref origin, value); }
    public bool HasInput => Name.Length > 0 || Basis.Length > 0;
}

public sealed class ImportDraft : ObservableObject
{
    private string name = "";
    private string rpm = "1800";
    private string unit = "dB";
    private string reference = "";
    private string condition = "";
    private string fileName = "粘贴输入.csv";
    private string csv = "";
    private DataOrigin origin;
    public string Name { get => name; set => Set(ref name, value); }
    public string Rpm { get => rpm; set => Set(ref rpm, value); }
    public string Unit { get => unit; set => Set(ref unit, value); }
    public string Reference { get => reference; set => Set(ref reference, value); }
    public string Condition { get => condition; set => Set(ref condition, value); }
    public string FileName { get => fileName; set => Set(ref fileName, value); }
    public string Csv { get => csv; set => Set(ref csv, value); }
    public DataOrigin Origin { get => origin; set => Set(ref origin, value); }
    public bool HasInput => Csv.Length > 0 || Name.Length > 0 || Reference.Length > 0 || Condition.Length > 0;

    public SpectrumMetadata Metadata()
    {
        return new(Name.Trim(), MotorDraft.Number(Rpm, "频谱转速"), Unit,
            Reference.Trim(), Condition.Trim(), Origin, FileName);
    }
}

public sealed class ReviewDraft : ObservableObject
{
    private string tolerance = "3";
    private string peakCount = "10";
    private string notes = "";
    public string Tolerance { get => tolerance; set => Set(ref tolerance, value); }
    public string PeakCount { get => peakCount; set => Set(ref peakCount, value); }
    public string Notes { get => notes; set => Set(ref notes, value); }
}

public sealed class SweepDraft : ObservableObject
{
    private string poles = "1,2,3";
    private string slots = "6,12,18";
    private string segments = "8,10,12";
    private string rpm = "1800";
    private string frequency = "60";
    private string tolerance = "3";
    public string Poles { get => poles; set => Set(ref poles, value); }
    public string Slots { get => slots; set => Set(ref slots, value); }
    public string Segments { get => segments; set => Set(ref segments, value); }
    public string Rpm { get => rpm; set => Set(ref rpm, value); }
    public string Frequency { get => frequency; set => Set(ref frequency, value); }
    public string Tolerance { get => tolerance; set => Set(ref tolerance, value); }
}

public sealed record Option<T>(T Value, string Label);
public sealed record NavigationItem(string Key, string Label, string Subtitle);
public sealed record ExportOption(string Key, string Label, string Extension, string Description);
