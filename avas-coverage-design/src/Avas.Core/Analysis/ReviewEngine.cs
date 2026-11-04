using System.Text;
using Avas.Core.Domain;

namespace Avas.Core.Analysis;

public sealed record ReviewFinding(string Severity, string Object, string Finding, string Action);

public static class ReviewEngine
{
    public static List<ReviewFinding> Inspect(Project project)
    {
        var findings = new List<ReviewFinding>();
        var settings = project.Settings;
        foreach (var source in project.Speakers)
        {
            if (!source.Enabled)
            {
                findings.Add(new("信息", source.Name, "当前源未启用，不参与声场和试听", "需要参与时到扬声器页启用并应用"));
                continue;
            }
            if (source.X < settings.MinX || source.X > settings.MaxX || source.Y < settings.MinY || source.Y > settings.MaxY)
                findings.Add(new("提示", source.Name, "声源位于显示网格外，仍参与计算", "核对坐标，或在背景与网格页扩大显示范围"));
            if (Acoustics.InsideVehicle(settings, source.X, source.Y))
                findings.Add(new("边界", source.Name, "声源位于车辆轮廓内，本模型仍向外自由传播", "不要将该结果解释为已包含车身遮挡；用实测修正设计"));
            if (source.Origin == Origin.Simulation)
                findings.Add(new("来源", source.Name, "使用模拟输入", "正式设计时替换为有来源的输入，保留模拟标识直至替换完成"));
            if (source.Origin == Origin.MeasurementDeclared)
                findings.Add(new("来源", source.Name, "实测来源仅为用户声明，软件未认证校准", "核对轴向参考距离、仪器和测量记录是否与频带一致"));
            if (source.Source.Contains("请填写", StringComparison.Ordinal))
                findings.Add(new("待补", source.Name, "来源仍是默认占位说明", "到扬声器页填写实际来源与轴向参考条件"));
            if (source.ReferenceDistance < settings.MinimumDistance)
                findings.Add(new("提示", source.Name, "参考距离小于传播距离下限", "核对近源截断设置，参考距离仍用于声级换算"));
            if (source.Bands.Any(x => Math.Abs(x.Correction) > 15))
                findings.Add(new("复核", source.Name, "至少一个频带修正幅度超过15dB", "核查是否重复叠加频响；15dB只是工程复核提示，不是判定限值"));
            if (source.Audio is not null)
                findings.Add(new("音频", source.Name, "已内嵌数字音频，物理频带仍独立维护", "核查录音内容与参考频谱是否对应；试听不证明标定关系"));
        }
        foreach (var group in project.Speakers.Where(x => x.Enabled).GroupBy(x => (x.X, x.Y, x.Height)).Where(x => x.Count() > 1))
            findings.Add(new("复核", string.Join("、", group.Select(x => x.Name)), "多个启用源位置完全相同", "确认是否有意叠加；复制源后常需调整坐标，相干效应未计算"));
        foreach (var point in project.Listeners)
        {
            if (Acoustics.InsideVehicle(settings, point.X, point.Y))
                findings.Add(new("边界", point.Name, "观察点位于车辆轮廓内", "本工具只给自由场点值，不能解释为车内声级"));
            if (point.X < settings.MinX || point.X > settings.MaxX || point.Y < settings.MinY || point.Y > settings.MaxY)
                findings.Add(new("信息", point.Name, "点位于网格外，点值仍计算但不计入网格覆盖", "需要计入区域时扩大网格，或保持其为独立观察点"));
            if (project.Speakers.Any(x => x.Enabled && Acoustics.Distance(x, point.X, point.Y, settings.ReceiverHeight) < settings.MinimumDistance))
                findings.Add(new("边界", point.Name, "接近至少一个声源，距离将被截断", "增大接收距离；不能将截断值视为近场解"));
        }
        if (settings.BackgroundSource.Contains("请填写", StringComparison.Ordinal))
            findings.Add(new("待补", "背景", "背景来源仍为默认占位说明", "在背景与网格页填写环境及频谱来源"));
        if (settings.SelectedHz == 0 && settings.AWeighted)
            findings.Add(new("口径", "合成", "使用中心频率A计权近似", "仅解释七个输入频带范围，不称全频积分声级"));
        if (project.Listeners.Count == 0)
            findings.Add(new("流程", "观察点", "没有独立观察点，无法查看点频谱或制作点试听", "在布局页添加点或追加等角点集"));
        findings.Add(new("固定假设", "背景", "背景在整个网格中均匀，多源按非相干能量叠加", "有空间非均匀、相干或真实车身影响时须使用其他模型或试验"));
        return findings;
    }
}

public sealed class TransectRequest
{
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double Heading { get; set; }
    public double Length { get; set; } = 10;
    public double Step { get; set; } = 0.5;
}

public sealed record TransectPoint(double Distance, PointResult Result);

public sealed class TransectResult
{
    public string InputHash { get; init; } = "";
    public string Metric { get; init; } = "";
    public string Source { get; init; } = "";
    public string Conditions { get; init; } = "";
    public List<TransectPoint> Points { get; init; } = [];
    public double? LastCovered => Points.Where(x => x.Result.Covered).Select(x => (double?)x.Distance).LastOrDefault();
    public int Transitions => Points.Zip(Points.Skip(1)).Count(x => x.First.Result.Covered != x.Second.Result.Covered);
}

public static class TransectEngine
{
    public static TransectResult Run(Project project, TransectRequest request)
    {
        var issues = Validation.Check(project, true);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("\n", issues));
        if (!double.IsFinite(request.StartX) || !double.IsFinite(request.StartY)
            || Math.Abs(request.StartX) > 100 || Math.Abs(request.StartY) > 100)
            throw new ArgumentException("剖面起点坐标必须在-100至100m范围");
        if (!double.IsFinite(request.Heading) || request.Heading is < -360 or > 360)
            throw new ArgumentException("剖面角度范围-360至360°");
        if (!double.IsFinite(request.Length) || request.Length is <= 0 or > 100)
            throw new ArgumentException("剖面长度必须大于0且不超过100m");
        if (!double.IsFinite(request.Step) || request.Step is < 0.1 or > 10)
            throw new ArgumentException("剖面步长范围0.1至10m");
        var count = (int)Math.Floor(request.Length / request.Step + 1e-9) + 1;
        if (count > 501)
            throw new ArgumentException("剖面最多501点，请增大步长");
        var angle = request.Heading * Math.PI / 180;
        var points = new List<TransectPoint>();
        for (var i = 0; i < count; i++)
        {
            var distance = i * request.Step;
            var x = request.StartX + distance * Math.Cos(angle);
            var y = request.StartY + distance * Math.Sin(angle);
            if (Math.Abs(x) > 100 || Math.Abs(y) > 100)
                throw new ArgumentException("剖面终点超出坐标范围，请缩短长度或调整起点");
            var point = FieldEngine.Evaluate(project, x, y, $"剖面{i + 1}");
            points.Add(new TransectPoint(distance, point));
        }
        return new TransectResult
        {
            InputHash = IO.ProjectCodec.InputHash(project),
            Metric = Acoustics.Metric(project.Settings),
            Source = IO.ProjectCodec.SourceSummary(project),
            Conditions = IO.ProjectCodec.Conditions(project.Settings),
            Points = points
        };
    }

    public static byte[] Csv(TransectResult result)
    {
        var text = new StringBuilder("\uFEFFModel,Version,Source,Conditions,InputHash,Metric,Distance_m,X_m,Y_m,Signal_dB,Margin_dB,EngineeringTargetMet,DistanceClamped,InVehicle\r\n");
        foreach (var point in result.Points)
        {
            var p = point.Result;
            text.AppendLine(string.Join(",", Software.Model, Software.Version, IO.SpectrumCsv.Cell(result.Source), IO.SpectrumCsv.Cell(result.Conditions), result.InputHash, IO.SpectrumCsv.Cell(result.Metric),
                ChartRenderer.Number(point.Distance), ChartRenderer.Number(p.X), ChartRenderer.Number(p.Y),
                ChartRenderer.Number(p.Signal), ChartRenderer.Number(p.Margin), p.Covered, p.NearFieldClamped, p.InVehicle));
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
