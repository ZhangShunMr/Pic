namespace Avas.Core.Domain;

public static class Demonstration
{
    public static Project Create()
    {
        var project = new Project
        {
            Name = "前部双扬声器布置示例",
            Owner = "演示操作员",
            Vehicle = "模拟车辆 EV-01",
            Purpose = "比较前部扬声器朝向对车前及侧前区域工程覆盖的影响",
            Note = "固定人工模拟频谱；不代表真实车辆，不用于合规结论"
        };
        project.Settings.BackgroundOrigin = Origin.Simulation;
        project.Settings.BackgroundSource = "软件固定模拟背景，各频带35dB";
        project.Speakers.Add(new Speaker
        {
            Name = "前左扬声器",
            X = 2.4,
            Y = 0.65,
            Heading = 25,
            Source = "固定模拟轴向1m声压级；不含车身作用",
            Origin = Origin.Simulation,
            Bands = Software.Frequencies.Select((hz, i) => new BandInput
            {
                Hz = hz,
                Level = new double[] { 49, 54, 60, 63, 61, 55, 47 }[i]
            }).ToList()
        });
        project.Speakers.Add(new Speaker
        {
            Name = "前右扬声器",
            X = 2.4,
            Y = -0.65,
            Heading = -25,
            Source = "固定模拟轴向1m声压级；不含车身作用",
            Origin = Origin.Simulation,
            Bands = project.Speakers[0].Bands.Select(x => x.Copy()).ToList()
        });
        project.Listeners =
        [
            new() { Name = "车前5m", X = 7.3, Y = 0 },
            new() { Name = "左前观察点", X = 5, Y = 4 },
            new() { Name = "右前观察点", X = 5, Y = -4 },
            new() { Name = "车后观察点", X = -5, Y = 0 }
        ];
        project.Audit.Add(new AuditEntry { Action = "载入固定模拟示例" });
        return project;
    }
}
