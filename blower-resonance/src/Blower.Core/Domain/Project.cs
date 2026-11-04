namespace Blower.Core.Domain;

public static class Software
{
    public const string Name = "空调鼓风机风量挡位与共振避让设计软件";
    public const string Version = "V1.0";
    public const int Schema = 1;
    public const string Boundary = "频率接近仅表示潜在风险；不代表实测响应、声压或噪声改善。";
    public static string Id() => Guid.NewGuid().ToString("N");
}

public enum Origin
{
    Manual,
    Simulation,
    DeclaredMeasurement
}

public enum OrderKind
{
    Shaft,
    Blade
}

public enum CheckState
{
    Pending,
    Scheduled,
    Completed,
    Recheck
}

public sealed class Project
{
    public int Schema { get; set; } = Software.Schema;
    public string Id { get; set; } = Software.Id();
    public string Name { get; set; } = "未命名设计项目";
    public string Owner { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Notes { get; set; } = "";
    public Origin Origin { get; set; } = Origin.Manual;
    public string Source { get; set; } = "用户录入；来源待补充";
    public bool Archived { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    public int Revision { get; set; }
    public DesignSettings Settings { get; set; } = new();
    public List<Excitation> Orders { get; set; } = [];
    public List<Mode> Modes { get; set; } = [];
    public List<SpeedPlan> Plans { get; set; } = [];
    public List<VerificationTask> Checks { get; set; } = [];
    public List<AuditEvent> Audit { get; set; } = [];
}

public sealed class DesignSettings
{
    public int Blades { get; set; } = 40;
    public double MinRpm { get; set; } = 500;
    public double MaxRpm { get; set; } = 5000;
    public double AbsoluteBandHz { get; set; } = 5;
    public double RelativeBandPercent { get; set; } = 3;
    public bool UseDamping { get; set; } = true;
    public double SpeedToleranceRpm { get; set; } = 20;
    public double MinimumGapRpm { get; set; } = 100;
    public double ScanStepRpm { get; set; } = 25;
    public double ScanWindowRpm { get; set; } = 250;
    public double ShiftWeight { get; set; } = 0.15;
}

public sealed class Excitation
{
    public string Id { get; set; } = Software.Id();
    public string Name { get; set; } = "旋转阶次";
    public OrderKind Kind { get; set; }
    public double Multiplier { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public string Evidence { get; set; } = "设计候选；待验证激励存在性";
    public double EffectiveOrder(int blades)
    {
        return Kind == OrderKind.Blade ? Multiplier * blades : Multiplier;
    }
}

public sealed class Mode
{
    public string Id { get; set; } = Software.Id();
    public string Name { get; set; } = "新模态";
    public double FrequencyHz { get; set; } = 600;
    public double? DampingRatio { get; set; }
    public double ExtraBandHz { get; set; }
    public bool Enabled { get; set; } = true;
    public Origin Origin { get; set; } = Origin.Manual;
    public string Source { get; set; } = "待补充模态来源与边界条件";
    public string Location { get; set; } = "";
}

public sealed class SpeedPlan
{
    public string Id { get; set; } = Software.Id();
    public string Name { get; set; } = "基准方案";
    public string Notes { get; set; } = "";
    public bool Locked { get; set; }
    public List<Gear> Gears { get; set; } = [];
}

public sealed class Gear
{
    public string Id { get; set; } = Software.Id();
    public string Name { get; set; } = "新挡位";
    public double Rpm { get; set; } = 1000;
    public bool Fixed { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class VerificationTask
{
    public string Id { get; set; } = Software.Id();
    public string Key { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string GearId { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Method { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Evidence { get; set; } = "";
    public CheckState State { get; set; }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Revision { get; set; }
}

public static class Labels
{
    public static string Source(Origin origin) => origin switch
    {
        Origin.Simulation => "模拟设计数据",
        Origin.DeclaredMeasurement => "用户声明实测（未认证）",
        _ => "人工设计输入"
    };

    public static string State(CheckState state) => state switch
    {
        CheckState.Scheduled => "已安排",
        CheckState.Completed => "已记录证据",
        CheckState.Recheck => "需复核",
        _ => "待验证"
    };

    public static string Kind(OrderKind kind)
    {
        return kind == OrderKind.Blade ? "叶片通过倍频" : "轴旋转阶次";
    }
}
