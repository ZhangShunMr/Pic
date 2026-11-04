namespace Blower.Core.Domain;

public static class Demo
{
    public static Project Create()
    {
        var project = new Project
        {
            Name = "模拟 · 40叶片鼓风机挡位设计",
            Vehicle = "演示平台 HVAC-A",
            Owner = "演示工程师",
            Origin = Origin.Simulation,
            Source = "内置可重复模拟参数，不是试验采集",
            Notes = "用于核对公式与操作；600Hz模态和40阶在900rpm精确交叉。"
        };
        project.Orders =
        [
            new() { Name = "轴频 1X", Kind = OrderKind.Shaft, Multiplier = 1 },
            new() { Name = "轴频 2X", Kind = OrderKind.Shaft, Multiplier = 2 },
            new() { Name = "叶片通过 BPF", Kind = OrderKind.Blade, Multiplier = 1 },
            new() { Name = "叶片二倍频", Kind = OrderKind.Blade, Multiplier = 2 }
        ];
        project.Modes =
        [
            new()
            {
                Name = "壳体面板 M1", FrequencyHz = 600, DampingRatio = 0.02,
                Location = "鼓风机壳体外侧", Origin = Origin.Simulation,
                Source = "模拟模态频率；非实测"
            },
            new()
            {
                Name = "风道局部 M2", FrequencyHz = 1200, DampingRatio = 0.015,
                Location = "出口风道连接处", Origin = Origin.Simulation,
                Source = "模拟模态频率；非实测"
            },
            new()
            {
                Name = "支架 M3", FrequencyHz = 90, DampingRatio = null,
                Location = "安装支架", Origin = Origin.Simulation,
                Source = "模拟模态频率；阻尼未知"
            }
        ];
        var baseline = new SpeedPlan { Name = "A · 初始挡位", Notes = "模拟初始设定" };
        var speeds = new double[] { 900, 1400, 1800, 2600, 3600, 4500 };
        for (var i = 0; i < speeds.Length; i++)
        {
            baseline.Gears.Add(new Gear { Name = $"{i + 1}挡", Rpm = speeds[i] });
        }
        var alternative = new SpeedPlan { Name = "B · 人工候选", Notes = "模拟候选；风量待验证" };
        var changed = new double[] { 1000, 1500, 1950, 2800, 3800, 4600 };
        for (var i = 0; i < changed.Length; i++)
        {
            alternative.Gears.Add(new Gear { Name = $"{i + 1}挡", Rpm = changed[i] });
        }
        project.Plans = [baseline, alternative];
        project.Audit.Add(new() { Action = "载入模拟示例", Detail = "40叶片，三模态，双方案" });
        return project;
    }
}
