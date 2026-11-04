namespace Cabin.Core.Domain;

public static class Software
{
    public const string Name = "车内低频声学模态与座位声压分布设计软件";
    public const string Version = "1.0";
    public const int Schema = 1;
    public const int MaximumBytes = 2 * 1024 * 1024;
    public const string Extension = ".cabin.json";
    public const string Scope = "理想刚性矩形腔；截断非零阶模态叠加；不含000均匀压缩模态；相对响应不等于实车绝对声压。";
    public const double ReferenceFrequency = 100;
    public const double ReferenceVolume = 1;
}

public sealed class Project
{
    public int Schema { get; set; } = Software.Schema;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名座舱";
    public string Revision { get; set; } = "A";
    public string Owner { get; set; } = "";
    public string DataSource { get; set; } = "人工输入 / 理想化几何";
    public bool IsDemo { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public Cavity Model { get; set; } = new();
    public List<Emitter> Sources { get; set; } = [];
    public List<EarPoint> Seats { get; set; } = [];
    public List<Snapshot> Baselines { get; set; } = [];
    public List<ReviewItem> Reviews { get; set; } = [];
    public List<JournalEntry> Journal { get; set; } = [];
}

public sealed class Cavity
{
    public double Length { get; set; } = 3.4;
    public double Width { get; set; } = 1.6;
    public double Height { get; set; } = 1.2;
    public double SoundSpeed { get; set; } = 343;
    public double Damping { get; set; } = 0.03;
    public double MinimumFrequency { get; set; } = 20;
    public double MaximumFrequency { get; set; } = 200;
    public double FrequencyStep { get; set; } = 1;
    public double Cutoff { get; set; } = 400;
    public double Volume => Length * Width * Height;
}

public sealed class Position
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public Position() { }
    public Position(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

public sealed class Emitter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "声源";
    public Position Point { get; set; } = new();
    public double Amplitude { get; set; } = 1;
    public double PhaseDegrees { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class EarPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "耳点";
    public Position Point { get; set; } = new();
    public string Note { get; set; } = "";
}

public sealed class Snapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "基准";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public Cavity Model { get; set; } = new();
    public List<Emitter> Sources { get; set; } = [];
    public List<EarPoint> Seats { get; set; } = [];
    public bool IsDemo { get; set; }
    public string DataSource { get; set; } = "";
}

public sealed class ReviewItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Target { get; set; } = "";
    public string Action { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Result { get; set; } = "待验证";
}

public sealed class JournalEntry
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
