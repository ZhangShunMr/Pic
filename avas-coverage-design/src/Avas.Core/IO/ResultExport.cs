using System.Text;
using Avas.Core.Analysis;
using Avas.Core.Domain;

namespace Avas.Core.IO;

public static class ResultExport
{
    private static string N(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static string H(string value) => ChartRenderer.Escape(value);

    public static void RequireCurrent(Project project, Calculation result)
    {
        if (ProjectCodec.InputHash(project) != result.InputHash)
            throw new ArgumentException("结果已过期，请重新计算后导出");
    }

    public static byte[] Csv(Project project, Calculation result, bool grid)
    {
        RequireCurrent(project, result);
        var csv = new StringBuilder("\uFEFFSoftware,Version,Model,Project,Revision,InputHash,Metric,Origin,Name,X_m,Y_m,Signal_dB,Background_dB,Combined_dB,Margin_dB,EngineeringTargetMet,DistanceClamped,InVehicle,125Hz_dB,250Hz_dB,500Hz_dB,1000Hz_dB,2000Hz_dB,4000Hz_dB,8000Hz_dB\r\n");
        var origin = string.Join(";", project.Speakers.Where(x => x.Enabled).Select(x => $"{x.Name}:{x.Origin.Label()}:{x.Source}"))
            + ";背景:" + project.Settings.BackgroundOrigin.Label() + ":" + project.Settings.BackgroundSource;
        foreach (var point in grid ? result.Grid : result.Listeners)
        {
            var fields = new[]
            {
                SpectrumCsv.Cell(Software.Name), Software.Version, Software.Model, SpectrumCsv.Cell(project.Name),
                project.Revision.ToString(), result.InputHash, SpectrumCsv.Cell(result.Metric), SpectrumCsv.Cell(origin),
                SpectrumCsv.Cell(point.Name), N(point.X), N(point.Y), N(point.Signal), N(point.Background), N(point.Combined),
                N(point.Margin), point.Covered.ToString(), point.NearFieldClamped.ToString(), point.InVehicle.ToString()
            }.Concat(point.Bands.Select(N));
            csv.AppendLine(string.Join(",", fields));
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    public static byte[] Report(Project project, Calculation result)
    {
        RequireCurrent(project, result);
        var text = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>声场工程估算报告</title><style>body{font:15px sans-serif;max-width:1100px;margin:40px auto;color:#24344d;padding:20px}h1{font-size:25px}h2{font-size:20px;margin-top:32px}table{border-collapse:collapse;width:100%;margin:15px 0}th,td{border:1px solid #ccd5df;padding:8px;text-align:left}th{background:#edf3f6}svg{width:100%}.note{padding:15px;background:#fff3d7}pre{white-space:pre-wrap;overflow-wrap:anywhere}@media print{h2{break-after:avoid}tr{break-inside:avoid}}</style><body>");
        text.Append($"<h1>{H(Software.Name)} V{Software.Version}</h1><p>工程估算报告 · {H(project.Name)}</p>");
        text.Append($"<p class='note'>{H(Software.Boundary)} 覆盖仅为用户阈值下的离散网格节点比例，不能解释为人群可听率。</p>");
        text.Append($"<p>项目标识 {project.Id}；修订 {project.Revision}；负责人 {H(project.Owner)}；车辆 {H(project.Vehicle)}；计算时间 {result.At:O}</p>");
        text.Append($"<p>目的：{H(project.Purpose)}</p><p>备注：{H(project.Note)}</p>");
        text.Append($"<p>模型 {Software.Model}；输入SHA256 {result.InputHash}</p>");
        text.Append("<h2>计算口径与来源</h2>");
        var s = project.Settings;
        text.Append($"<p>范围X={N(s.MinX)}至{N(s.MaxX)}m，Y={N(s.MinY)}至{N(s.MaxY)}m；步长{N(s.Step)}m；接收高度{N(s.ReceiverHeight)}m；距离下限{N(s.MinimumDistance)}m。X正向车前，Y正向车左。</p>");
        text.Append($"<p>轮廓{N(s.VehicleLength)}×{N(s.VehicleWidth)}m；内部节点排除={s.ExcludeVehicle}。指标：{H(result.Metric)}；声级目标{N(s.LevelTarget)}dB，信号背景差目标{N(s.MarginTarget)}dB，须同时满足。</p>");
        text.Append($"<p>背景来源：{H(s.BackgroundOrigin.Label())} / {H(s.BackgroundSource)}</p>");
        text.Append("<table><tr><th>扬声器与来源</th><th>位置与朝向</th><th>参考与指向性</th><th>音频</th></tr>");
        foreach (var speaker in project.Speakers)
        {
            text.Append($"<tr><td>{H(speaker.Name)}；启用={speaker.Enabled}<br>{H(speaker.Origin.Label())} / {H(speaker.Source)}</td><td>({N(speaker.X)},{N(speaker.Y)},{N(speaker.Height)})m；{N(speaker.Heading)}°</td><td>r0={N(speaker.ReferenceDistance)}m；增益{N(speaker.Gain)}dB；n={N(speaker.Exponent)}；floor={N(speaker.RearFloor)}</td><td>{H(speaker.Audio?.Name ?? "无，试听使用模拟噪声")}</td></tr>");
        }
        text.Append("</table><h2>输入频带</h2><table><tr><th>对象</th><th>Hz</th><th>输入声级 dB</th><th>频响修正 dB</th></tr>");
        foreach (var speaker in project.Speakers)
            foreach (var band in speaker.Bands.OrderBy(x => x.Hz))
                text.Append($"<tr><td>{H(speaker.Name)}</td><td>{band.Hz}</td><td>{N(band.Level)}</td><td>{N(band.Correction)}</td></tr>");
        foreach (var band in s.Background.OrderBy(x => x.Hz))
            text.Append($"<tr><td>背景</td><td>{band.Hz}</td><td>{N(band.Level)}</td><td>0</td></tr>");
        text.Append("</table><h2>布局与声场</h2>");
        text.Append(ChartRenderer.Map(project, result));
        text.Append($"<p>有效网格{result.Grid.Count}，满足{result.CoveredCount}；覆盖{result.Coverage:F2}%；声级范围{result.Minimum:F2}至{result.Maximum:F2}dB；能量平均{result.MeanEnergy:F2}dB；截断距离节点{result.ClampedCount}个。</p>");
        text.Append("<h2>观察点</h2><table><tr><th>观察点</th><th>坐标 m</th><th>警示声 dB</th><th>背景 dB</th><th>合成 dB</th><th>背景差 dB</th><th>工程目标</th></tr>");
        foreach (var point in result.Listeners)
            text.Append($"<tr><td>{H(point.Name)}</td><td>{N(point.X)},{N(point.Y)}</td><td>{point.Signal:F3}</td><td>{point.Background:F3}</td><td>{point.Combined:F3}</td><td>{point.Margin:F3}</td><td>{(point.Covered ? "满足" : "未满足")}{(point.NearFieldClamped ? "；距离截断" : "")}{(point.InVehicle ? "；车内示意点" : "")}</td></tr>");
        text.Append("</table>");
        foreach (var point in result.Listeners)
        {
            text.Append($"<h2>{H(point.Name)}频谱及贡献</h2>");
            text.Append(ChartRenderer.Spectrum(point, s));
            text.Append("<table><tr><th>源</th><th>几何距离 m</th><th>传播与指向修正 dB</th><th>当前指标声级 dB</th></tr>");
            foreach (var source in point.Contributions)
                text.Append($"<tr><td>{H(source.Name)}</td><td>{source.Distance:F3}</td><td>{source.Loss:F3}</td><td>{Acoustics.Select(source.Bands, s):F3}</td></tr>");
            text.Append("</table>");
        }
        text.Append("<h2>方法与限制</h2><p>每带 L=Lref+频响修正+增益−20log10(max(r,rmin)/r0)+20log10(floor+(1−floor)max(0,cosθ)^n)。n=0定义为全向。多个启用源按10log10(Σ10^(L/10))叠加。A计权仅作用于七带合成，有限带合成不等于全频总值。背景差=警示声−背景声；合成声级为两者能量叠加。色图为离散网格展示，边缘单元为示意，未作连续面积积分。</p>");
        text.Append("<ul>");
        foreach (var warning in result.Warnings)
            text.Append($"<li>{H(warning)}</li>");
        text.Append("</ul><p>下一步应以本报告记录的方案制作样件，并在真实安装与环境条件下独立测量。报告本身不提供实际效果保证。</p>");
        text.Append("<h2>修订审计</h2><table><tr><th>时间 UTC</th><th>修订</th><th>操作</th></tr>");
        foreach (var entry in project.Audit.TakeLast(30))
            text.Append($"<tr><td>{entry.At:O}</td><td>{entry.Revision}</td><td>{H(entry.Action)}</td></tr>");
        text.Append("</table></body></html>");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static byte[] ComparisonCsv(Comparison comparison)
    {
        if (!comparison.Comparable)
            throw new ArgumentException("条件不一致，无法导出比较差值");
        var text = new StringBuilder("\uFEFFModel,Version,Metric,BaselineInputHash,TargetInputHash,BaselineSource,TargetSource,Conditions,Baseline,Target,Point,X_m,Y_m,Baseline_dB,Target_dB,TargetMinusBaseline_dB\r\n");
        foreach (var point in comparison.Points)
            text.AppendLine(string.Join(",", Software.Model, Software.Version, SpectrumCsv.Cell(comparison.Baseline!.Metric), comparison.Baseline.InputHash, comparison.Target!.InputHash,
                SpectrumCsv.Cell(comparison.BaselineSource), SpectrumCsv.Cell(comparison.TargetSource), SpectrumCsv.Cell(comparison.Conditions), SpectrumCsv.Cell(comparison.BaselineName), SpectrumCsv.Cell(comparison.TargetName),
                SpectrumCsv.Cell(point.Name), N(point.X), N(point.Y), N(point.Baseline), N(point.Target), N(point.Difference)));
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static byte[] ScanCsv(ScanResult scan)
    {
        var text = new StringBuilder("\uFEFFModel,Version,Metric,Source,Conditions,Speaker,Parameter,InputHash,Value,CoveragePercent,Minimum_dB,Maximum_dB,WorstMargin_dB,CoveredListeners\r\n");
        foreach (var row in scan.Rows)
            text.AppendLine(string.Join(",", Software.Model, Software.Version, SpectrumCsv.Cell(scan.Metric), SpectrumCsv.Cell(scan.Source), SpectrumCsv.Cell(scan.Conditions), SpectrumCsv.Cell(scan.SpeakerName), SpectrumCsv.Cell(ScanEngine.Label(scan.Parameter)), scan.InputHash,
                N(row.Value), N(row.Coverage), N(row.Minimum), N(row.Maximum), N(row.WorstMargin), row.CoveredPoints));
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
