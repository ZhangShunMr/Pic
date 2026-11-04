namespace TrimContact.Core.Domain;

public static class Demonstration
{
    public static DesignProject Create()
    {
        var project = new DesignProject
        {
            Name = "门板四卡扣接触布置演示",
            Owner = "演示工程师",
            Goal = "比较缓冲块预紧量对接触保持和卡扣反力的影响",
            Origin = DataOrigin.Simulation,
            Source = "内置固定模拟参数；非实测，不证明实车效果",
            Panel = new Panel
            {
                Name = "400×300 mm刚性饰板示例",
                MassKg = 1,
                CenterXmm = 200,
                CenterYmm = 150,
                Outline = [new() { Xmm = 0, Ymm = 0 }, new() { Xmm = 400, Ymm = 0 },
                    new() { Xmm = 400, Ymm = 300 }, new() { Xmm = 0, Ymm = 300 }]
            },
            Loads = [new LoadCase { Name = "一倍重力周期演示", Source = "模拟正弦惯性载荷" }]
        };
        var variant = new DesignVariant { Name = "A 基准接触布置" };
        AddClip(variant, "左下卡扣", 40, 40);
        AddClip(variant, "右下卡扣", 360, 40);
        AddClip(variant, "右上卡扣", 360, 260);
        AddClip(variant, "左上卡扣", 40, 260);
        variant.Contacts.Add(new ContactPoint
        {
            Name = "中心缓冲块",
            Xmm = 200,
            Ymm = 150,
            GapMm = 0.1,
            PreloadMm = 0.2,
            ToleranceMm = 0.15,
            NormalStiffness = 100,
            TangentialStiffness = 40,
            Friction = 0.3
        });
        project.Variants.Add(variant);
        project.Audit.Add(new AuditEntry { Action = "载入内置模拟演示" });
        return project;
    }

    public static void AddClip(DesignVariant variant, string name, double x, double y)
    {
        variant.Clips.Add(new Clip { Name = name, Xmm = x, Ymm = y });
    }
}
