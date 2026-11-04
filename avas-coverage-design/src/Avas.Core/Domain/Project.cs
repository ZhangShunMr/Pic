namespace Avas.Core.Domain;

public static class Software
{
    public const string Name = "车外行人警示声音传播与覆盖设计软件";
    public const string Version = "2.0";
    public const string Model = "free-field-incoherent-1";
    public const string Boundary = "自由声场工程估算；未模拟车身遮挡、反射、衍射、地面与气象；非可听性或法规判定。";
    public static readonly int[] Frequencies = [125, 250, 500, 1000, 2000, 4000, 8000];
    public static readonly double[] AWeights = [-16.1, -8.6, -3.2, 0, 1.2, 1, -1.1];
}

public enum Origin
{
    Manual,
    Simulation,
    MeasurementDeclared,
    Estimate
}

public static class OriginNames
{
    public static string Label(this Origin origin) => origin switch
    {
        Origin.Manual => "人工输入",
        Origin.Simulation => "模拟示例",
        Origin.MeasurementDeclared => "实测（用户声明）",
        Origin.Estimate => "估算",
        _ => "未知"
    };
}

public sealed class BandInput
{
    public int Hz { get; set; }
    public double Level { get; set; } = 60;
    public double Correction { get; set; }
    public BandInput Copy() => new() { Hz = Hz, Level = Level, Correction = Correction };
}

public sealed class Speaker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新扬声器";
    public bool Enabled { get; set; } = true;
    public double X { get; set; } = 2.3;
    public double Y { get; set; }
    public double Height { get; set; } = 0.5;
    public double Heading { get; set; }
    public double ReferenceDistance { get; set; } = 1;
    public double Gain { get; set; }
    public double Exponent { get; set; } = 1;
    public double RearFloor { get; set; } = 0.2;
    public Origin Origin { get; set; } = Origin.Manual;
    public string Source { get; set; } = "请填写轴向声压级来源与参考条件";
    public string Note { get; set; } = "";
    public List<BandInput> Bands { get; set; } = Software.Frequencies.Select(hz => new BandInput { Hz = hz }).ToList();
    public AudioAsset? Audio { get; set; }
}

public sealed class Listener
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "观察点";
    public double X { get; set; } = 5;
    public double Y { get; set; }
    public string Note { get; set; } = "";
}

public sealed class Settings
{
    public double MinX { get; set; } = -10;
    public double MaxX { get; set; } = 10;
    public double MinY { get; set; } = -8;
    public double MaxY { get; set; } = 8;
    public double Step { get; set; } = 0.5;
    public double ReceiverHeight { get; set; } = 1.5;
    public double MinimumDistance { get; set; } = 0.5;
    public double VehicleLength { get; set; } = 4.6;
    public double VehicleWidth { get; set; } = 1.8;
    public bool ExcludeVehicle { get; set; } = true;
    public int SelectedHz { get; set; }
    public bool AWeighted { get; set; }
    public double LevelTarget { get; set; } = 45;
    public double MarginTarget { get; set; } = 3;
    public string BackgroundSource { get; set; } = "请填写背景声来源";
    public Origin BackgroundOrigin { get; set; } = Origin.Manual;
    public List<BandInput> Background { get; set; } = Software.Frequencies.Select(hz => new BandInput { Hz = hz, Level = 35 }).ToList();
    public AudioAsset? BackgroundAudio { get; set; }
}

public sealed class AudioAsset
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Data { get; set; } = "";
    public int Rate { get; set; }
    public int Channels { get; set; }
    public double Duration { get; set; }
    public string Source { get; set; } = "用户导入数字音频，未标定";
}

public sealed class Snapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public string InputHash { get; set; } = "";
    public Settings Settings { get; set; } = new();
    public List<Speaker> Speakers { get; set; } = [];
    public List<Listener> Listeners { get; set; } = [];
}

public sealed class AuditEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public int Revision { get; set; }
}

public sealed class Project
{
    public int Schema { get; set; } = 1;
    public string SoftwareVersion { get; set; } = Software.Version;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名项目";
    public string Owner { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Archived { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public Settings Settings { get; set; } = new();
    public List<Speaker> Speakers { get; set; } = [];
    public List<Listener> Listeners { get; set; } = [];
    public List<Snapshot> Snapshots { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}
