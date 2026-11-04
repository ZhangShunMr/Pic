using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class Labels
{
    public static string Topology(MotorTopology value) => value switch
    {
        MotorTopology.BrushlessPermanentMagnet => "无刷永磁同步电机",
        MotorTopology.BrushedPermanentMagnet => "有刷永磁直流电机",
        _ => "未知拓扑"
    };

    public static string Origin(DataOrigin value) => value switch
    {
        DataOrigin.Manual => "人工填写",
        DataOrigin.MeasuredDeclared => "实测（用户声明）",
        DataOrigin.Simulated => "模拟演示",
        DataOrigin.Estimated => "估算",
        _ => "未知来源"
    };

    public static string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public static string ObjectName(string key) => key switch
    {
        "rotor" => "转子与磁极",
        "slots" => "槽与齿",
        "brush" => "换向片与电刷",
        "speed" => "转速与观察范围",
        "project" => "项目档案",
        _ => "自定义候选"
    };
}
