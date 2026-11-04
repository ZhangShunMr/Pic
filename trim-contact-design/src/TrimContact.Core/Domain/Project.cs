namespace TrimContact.Core.Domain;

public static class Product
{
    public const string Name = "汽车内饰卡扣与防异响接触设计软件";
    public const string Version = "V1.0";
    public const string Method = "RigidContact-1.0";
    public const int Schema = 1;
    public const double Gravity = 9.80665;
    public const double ContactEpsilon = 1e-7;
    public const string Boundary = "刚性饰板、准静态采样、单方向切向接触筛查；不预测真实异响声压，不替代样件验证。";
}

public enum DataOrigin
{
    Simulation,
    MeasurementDeclared,
    Estimate,
    Manual
}

public static class OriginNames
{
    public static string Label(this DataOrigin origin) => origin switch
    {
        DataOrigin.Simulation => "模拟演示",
        DataOrigin.MeasurementDeclared => "实测（用户声明）",
        DataOrigin.Estimate => "工程估算",
        _ => "人工填写"
    };
}

public sealed class DesignProject
{
    public int Schema { get; set; } = Product.Schema;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "未命名接触设计";
    public string Revision { get; set; } = "A";
    public string Owner { get; set; } = "";
    public string Goal { get; set; } = "";
    public DataOrigin Origin { get; set; } = DataOrigin.Manual;
    public string Source { get; set; } = "待填写参数依据";
    public bool Archived { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    public Panel Panel { get; set; } = new();
    public CalculationSettings Settings { get; set; } = new();
    public List<DesignVariant> Variants { get; set; } = [];
    public List<LoadCase> Loads { get; set; } = [];
    public List<VerificationTask> Tasks { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}

public sealed class Panel
{
    public string Name { get; set; } = "待定义饰板";
    public double MassKg { get; set; } = 1;
    public double CenterXmm { get; set; } = 200;
    public double CenterYmm { get; set; } = 150;
    public List<Vertex> Outline { get; set; } = [];
}

public sealed class Vertex
{
    public double Xmm { get; set; }
    public double Ymm { get; set; }
}

public sealed class DesignVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新方案";
    public string Note { get; set; } = "";
    public List<Clip> Clips { get; set; } = [];
    public List<ContactPoint> Contacts { get; set; } = [];
}

public sealed class Clip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "卡扣";
    public double Xmm { get; set; }
    public double Ymm { get; set; }
    public double NormalStiffness { get; set; } = 100;
    public double TangentialStiffness { get; set; } = 50;
    public double ForceLimitN { get; set; } = 80;
}

public sealed class ContactPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "接触点";
    public string Material { get; set; } = "缓冲块（待标定）";
    public double Xmm { get; set; }
    public double Ymm { get; set; }
    public double GapMm { get; set; } = 0.1;
    public double ToleranceMm { get; set; } = 0.05;
    public double PreloadMm { get; set; } = 0.2;
    public double NormalStiffness { get; set; } = 80;
    public double TangentialStiffness { get; set; } = 30;
    public double Friction { get; set; } = 0.3;
    public double CompressionLimitMm { get; set; } = 0.8;
}

public sealed class LoadCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新载荷";
    public double NormalBiasG { get; set; }
    public double NormalAmplitudeG { get; set; } = 1;
    public double TangentialBiasG { get; set; }
    public double TangentialAmplitudeG { get; set; } = 0.5;
    public double PhaseDegrees { get; set; } = 90;
    public double FrequencyHz { get; set; } = 10;
    public string Source { get; set; } = "工程估算，待测量";
}

public sealed class CalculationSettings
{
    public int Phases { get; set; } = 24;
    public bool EnumerateTolerance { get; set; } = true;
}

public sealed class VerificationTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PointId { get; set; }
    public string Title { get; set; } = "新验证任务";
    public string Method { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public string Owner { get; set; } = "";
    public bool Completed { get; set; }
    public string Evidence { get; set; } = "";
}

public sealed class AuditEntry
{
    public DateTimeOffset Utc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
}
