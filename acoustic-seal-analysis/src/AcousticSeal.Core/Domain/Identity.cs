using System.Globalization;

namespace AcousticSeal.Core.Domain;

public static class Software
{
    public const string Name = "车身声学泄漏路径与封堵试验分析软件";
    public const string Version = "3.0.0";
    public const string DisplayVersion = "V3.0";
    public const int SchemaVersion = 2;
    public const string Extension = ".sealproj";
    public const string LevelUnit = "dB re 20 µPa";
    public const string Limitation = "结果为封堵前后测得声级差，不是路径独立能量贡献；单项分贝不可相加预测组合。";
    public const string ScreeningNote = "用户工程筛查参数，不是标准合格判定。";
    public const int MaximumTrials = 2000;
    public const int MaximumBands = 200;
    public const int MaximumPairs = 5000;
    public const int MaximumFileBytes = 12 * 1024 * 1024;

    public static string Format(double? value, int digits = 3)
    {
        return value.HasValue
            ? value.Value.ToString("F" + digits, CultureInfo.InvariantCulture)
            : "—";
    }

    public static string Time(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }
}

public enum TrialKind
{
    Baseline,
    Single,
    Combination,
    Recovery
}

public enum SourceKind
{
    Simulation,
    Measured,
    Manual,
    Estimated
}

public enum PairPurpose
{
    Effect,
    Drift,
    Recovery
}

public enum ProjectStatus
{
    Active,
    Archived
}

public enum ResultStatus
{
    Valid,
    Missing,
    Blocked,
    Empty,
    Within,
    Alert,
    Insufficient
}

public enum IssueSeverity
{
    Information,
    Warning,
    Error
}

public enum ImportMode
{
    Merge,
    Replace
}

public static class Labels
{
    public static string Text(this TrialKind value)
    {
        return value switch
        {
            TrialKind.Baseline => "基线",
            TrialKind.Single => "单项封堵",
            TrialKind.Combination => "组合封堵",
            TrialKind.Recovery => "解除封堵恢复",
            _ => "未知类型"
        };
    }

    public static string Text(this SourceKind value)
    {
        return value switch
        {
            SourceKind.Simulation => "模拟",
            SourceKind.Measured => "实测（用户声明）",
            SourceKind.Manual => "人工填写",
            SourceKind.Estimated => "估算",
            _ => "未知来源"
        };
    }

    public static string Text(this PairPurpose value)
    {
        return value switch
        {
            PairPurpose.Effect => "封堵声级差",
            PairPurpose.Drift => "基线漂移",
            PairPurpose.Recovery => "解除封堵恢复",
            _ => "未知用途"
        };
    }

    public static string Text(this ResultStatus value)
    {
        return value switch
        {
            ResultStatus.Valid => "可比较",
            ResultStatus.Missing => "缺失",
            ResultStatus.Blocked => "已阻断",
            ResultStatus.Empty => "无有效数据",
            ResultStatus.Within => "阈值内",
            ResultStatus.Alert => "超过阈值",
            ResultStatus.Insufficient => "样本不足",
            _ => "未知状态"
        };
    }

    public static string Text(this IssueSeverity value)
    {
        return value switch
        {
            IssueSeverity.Error => "阻断",
            IssueSeverity.Warning => "警示",
            _ => "提示"
        };
    }

    public static string Css(this ResultStatus value)
    {
        return value switch
        {
            ResultStatus.Valid or ResultStatus.Within => "good",
            ResultStatus.Blocked or ResultStatus.Alert => "bad",
            _ => "neutral"
        };
    }
}
