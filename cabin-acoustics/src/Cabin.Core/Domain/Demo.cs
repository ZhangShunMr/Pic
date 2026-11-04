using Cabin.Core.IO;

namespace Cabin.Core.Domain;

public static class Demo
{
    public static Project Create()
    {
        var project = new Project
        {
            Name = "四座矩形座舱 · 模拟研究",
            Owner = "示例工程师",
            IsDemo = true,
            DataSource = "可重复模拟；矩形理想几何；非实测",
            Notes = "比较前后排耳点响应；将声源移至x=Lx/2可观察奇数纵向模态耦合减弱。",
            Sources =
            [
                new() { Name = "前舱等效声源", Point = new(0.2, 0.8, 0.35) }
            ],
            Seats =
            [
                new() { Name = "驾驶员", Point = new(1.05, 0.4, 0.85), Note = "模拟耳点" },
                new() { Name = "前排乘员", Point = new(1.05, 1.2, 0.85), Note = "模拟耳点" },
                new() { Name = "后排左", Point = new(2.55, 0.4, 0.85), Note = "模拟耳点" },
                new() { Name = "后排右", Point = new(2.55, 1.2, 0.85), Note = "模拟耳点" }
            ]
        };
        project.Reviews.Add(new()
        {
            Target = "首阶纵向模态约50.441Hz",
            Action = "沿x方向布置前、中、后测点，保持y和z一致；实测检查相位和幅值分布。",
            Result = "待实测"
        });
        project.Journal.Add(new() { Action = "载入独立示例", Detail = "所有尺寸及点位均为模拟" });
        project.Baselines.Add(Capture(project, "原始模拟布置"));
        return project;
    }

    public static Snapshot Capture(Project project, string name)
    {
        return new()
        {
            Name = name,
            Model = ProjectCodec.Clone(project.Model),
            Sources = ProjectCodec.Clone(project.Sources),
            Seats = ProjectCodec.Clone(project.Seats),
            IsDemo = project.IsDemo,
            DataSource = project.DataSource
        };
    }

    public static Project FromSnapshot(Snapshot snapshot)
    {
        return new()
        {
            Name = snapshot.Name,
            Model = ProjectCodec.Clone(snapshot.Model),
            Sources = ProjectCodec.Clone(snapshot.Sources),
            Seats = ProjectCodec.Clone(snapshot.Seats),
            IsDemo = snapshot.IsDemo,
            DataSource = snapshot.DataSource
        };
    }
}
