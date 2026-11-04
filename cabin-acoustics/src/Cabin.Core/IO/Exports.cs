using System.Net;
using System.Text;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public static class Exports
{
    public static string ModesCsv(Project project, AnalysisResult result)
    {
        var builder = Metadata(project, "模态频率；无000模态；退化模态逐项保留");
        builder.AppendLine("nx,ny,nz,frequency_Hz,kind,degeneracy,norm_factor");
        foreach (var mode in result.Modes)
            builder.AppendLine(PointCsv.Join(mode.Nx.ToString(), mode.Ny.ToString(), mode.Nz.ToString(),
                PointCsv.N(mode.Frequency), mode.Kind, mode.Degeneracy.ToString(), PointCsv.N(mode.NormFactor)));
        return builder.ToString();
    }

    public static string ResponsesCsv(Project project, AnalysisResult result)
    {
        var builder = Metadata(project, "无量纲截断响应；所有耳点共用全频带最大幅值参考；非dB SPL");
        builder.AppendLine("seat_id,seat_name,frequency_Hz,real,imaginary,magnitude,phase_deg,relative_dB,reference_magnitude");
        foreach (var curve in result.Curves)
        {
            foreach (var point in curve.Points)
                builder.AppendLine(PointCsv.Join(curve.Id.ToString(), curve.Name, PointCsv.N(point.Frequency),
                    PointCsv.N(point.Real), PointCsv.N(point.Imaginary), PointCsv.N(point.Magnitude),
                    PointCsv.N(point.Phase), PointCsv.N(point.RelativeDb(result.Reference)), PointCsv.N(result.Reference)));
        }
        return builder.ToString();
    }

    public static string ContributionsCsv(Project project, AnalysisResult result, EarPoint seat, double frequency)
    {
        var builder = Metadata(project, $"耳点{seat.Name}；频率{PointCsv.N(frequency)}Hz；各项复数相加，幅值不能直接相加");
        builder.AppendLine("mode,frequency_Hz,term_magnitude,term_phase_deg,source_coupling_magnitude,seat_shape");
        foreach (var item in Engine.Contributions(project, result, seat, frequency))
            builder.AppendLine(PointCsv.Join(item.Key, PointCsv.N(item.Frequency), PointCsv.N(item.Magnitude),
                PointCsv.N(item.Phase), PointCsv.N(item.SourceMagnitude), PointCsv.N(item.SeatShape)));
        return builder.ToString();
    }

    public static string ScanCsv(Project project, ScanResult result)
    {
        var builder = Metadata(project, "单参数扫描；其他参数固定；失败行保留原因；耳点=" + result.Settings.SeatId);
        builder.AppendLine(PointCsv.Join("parameter=" + result.Settings.Parameter, "unit=" + result.Unit));
        builder.AppendLine("value,peak_magnitude,peak_frequency_Hz,rms_magnitude,first_mode_Hz,error");
        foreach (var row in result.Rows)
            builder.AppendLine(PointCsv.Join(PointCsv.N(row.Parameter), PointCsv.N(row.PeakMagnitude),
                PointCsv.N(row.PeakFrequency), PointCsv.N(row.Rms), PointCsv.N(row.FirstMode), row.Error));
        return builder.ToString();
    }

    public static string ComparisonCsv(Project project, Snapshot baseline, ComparisonResult result)
    {
        var builder = Metadata(project, "比较基准=" + baseline.Name + "；同频带同截断；差值为当前/基准幅值比");
        foreach (var warning in result.Warnings)
            builder.AppendLine(PointCsv.Join("#警示", warning));
        builder.AppendLine("seat,baseline_peak,current_peak,peak_ratio_dB,baseline_peak_Hz,current_peak_Hz,rms_ratio_dB");
        foreach (var row in result.Seats)
            builder.AppendLine(PointCsv.Join(row.Name, PointCsv.N(row.BaselinePeak), PointCsv.N(row.CurrentPeak),
                PointCsv.N(row.PeakRatioDb), PointCsv.N(row.BaselineFrequency), PointCsv.N(row.CurrentFrequency), PointCsv.N(row.RmsRatioDb)));
        return builder.ToString();
    }

    public static string ReviewsCsv(Project project)
    {
        var builder = Metadata(project, "实测复核清单；清单内容为人工记录，不自动判定合格");
        builder.AppendLine("target,action,owner,result");
        foreach (var row in project.Reviews)
            builder.AppendLine(PointCsv.Join(row.Target, row.Action, row.Owner, row.Result));
        return builder.ToString();
    }

    public static string Html(Project project, AnalysisResult result, ComparisonResult? comparison = null, ScanResult? scan = null)
    {
        var builder = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'>");
        builder.Append("<title>座舱声学研究报告</title><style>body{font:15px/1.7 sans-serif;color:#243d40;max-width:1100px;margin:40px auto;padding:24px}h1{font-size:27px}h2{margin-top:30px}table{border-collapse:collapse;width:100%;margin:12px 0}td,th{padding:8px;border:1px solid #bccbc9;text-align:left}th{background:#e9eeea}.limit{padding:16px;background:#f7edda}svg{width:100%;height:auto}small{color:#536c70}@media print{body{margin:0}tr{break-inside:avoid}}</style>");
        builder.Append("<h1>" + H(Software.Name) + " V" + Software.Version + "</h1>");
        builder.Append("<p>项目：" + H(project.Name) + " / 修订 " + H(project.Revision) + " / 负责人：" + H(project.Owner) + "</p>");
        builder.Append("<p>来源：" + H(project.DataSource) + " / " + (project.IsDemo ? "模拟示例" : "人工模型") + " / 生成UTC：" + DateTimeOffset.UtcNow.ToString("u") + "</p>");
        builder.Append("<p class='limit'>" + H(Software.Scope) + " 图形和幅值仅用于简化模型对比。结果未校准到Pa或dB SPL。</p>");
        builder.Append("<h2>1. 模型与计算口径</h2>");
        Table(builder, ["参数", "数值与单位"],
        [
            ["长度Lx / 宽度Ly / 高度Lz", $"{N(project.Model.Length)} / {N(project.Model.Width)} / {N(project.Model.Height)} m"],
            ["声速", N(project.Model.SoundSpeed) + " m/s"],
            ["模态阻尼比", N(project.Model.Damping)],
            ["频带及步长", $"{N(project.Model.MinimumFrequency)}–{N(project.Model.MaximumFrequency)} Hz；{N(project.Model.FrequencyStep)} Hz"],
            ["模态截断", N(project.Model.Cutoff) + " Hz"],
            ["幅值参考", N(result.Reference) + "；本项目所有耳点全频带最大值"],
            ["计算时间UTC", result.Computed.ToString("u")]
        ]);
        builder.Append("<p>f(n)=c/2·√[(nx/Lx)²+(ny/Ly)²+(nz/Lz)²]；φ=cos(nxπx/Lx)cos(nyπy/Ly)cos(nzπz/Lz)。坐标原点前左下，x向后，y向右，z向上。非零模态按频率截断，退化模态不合并。</p>");
        builder.Append("<p>H=Σ (2^d·1m³/V)·φ(r)·Σ[A·exp(iθ)·φ(s)]·100²/[fn²−f²+i·2ζfnf]。d为非零阶次数量。采用固定100Hz与1m³标度，V为腔体体积，保持同一源系数跨尺寸比较的范数一致；无量纲；不含源强校准和000压缩模态。相对dB=20log10(|H|/共同参考)，显示下限−120dB；近零参考显示−120dB并需按零响应理解。</p>");
        builder.Append("<h2>2. 声源与耳点</h2>");
        Table(builder, ["类型", "名称", "x / m", "y / m", "z / m", "条件"],
            project.Sources.Select(x => new[] { "声源", x.Name, N(x.Point.X), N(x.Point.Y), N(x.Point.Z), $"A={N(x.Amplitude)}；θ={N(x.PhaseDegrees)}°；{(x.Enabled ? "启用" : "停用")}" })
            .Concat(project.Seats.Select(x => new[] { "耳点", x.Name, N(x.Point.X), N(x.Point.Y), N(x.Point.Z), x.Note })));
        builder.Append("<h2>3. 模态频率（前40阶）</h2>");
        Table(builder, ["阶次nx,ny,nz", "频率 / Hz", "类型", "退化重数"],
            result.Modes.Take(40).Select(x => new[] { x.Key, N(x.Frequency), x.Kind, x.Degeneracy.ToString() }));
        builder.Append("<h2>4. 座位相对响应</h2>");
        builder.Append(ResponseSvg(project, result));
        Table(builder, ["耳点", "峰值频率 / Hz", "峰值幅值", "峰值相对dB", "频点等权RMS"],
            result.Curves.Select(x => new[] { x.Name, N(x.Peak.Frequency), N(x.Peak.Magnitude), N(x.Peak.RelativeDb(result.Reference)), N(x.Rms) }));
        builder.Append("<p>峰值为离散频率网格最大值，未进行峰值插值；RMS为采样频点幅值平方均值开方，不等同于宽带实测声压。步长可能漏掉窄峰，应细化频率网格并检查截断敏感性。</p>");
        if (comparison is not null)
        {
            builder.Append("<h2>5. 当前比较结果</h2>");
            foreach (var warning in comparison.Warnings)
                builder.Append("<p class='limit'>" + H(warning) + "</p>");
            Table(builder, ["耳点", "基准峰值", "当前峰值", "峰值比 / dB", "RMS比 / dB"],
                comparison.Seats.Select(x => new[] { x.Name, N(x.BaselinePeak), N(x.CurrentPeak), N(x.PeakRatioDb), N(x.RmsRatioDb) }));
        }
        if (scan is not null)
        {
            builder.Append("<h2>6. 参数扫描：" + H(scan.Settings.Parameter) + "</h2>");
            Table(builder, ["参数 / " + scan.Unit, "峰值幅值", "峰值频率 / Hz", "首阶模态 / Hz", "状态"],
                scan.Rows.Select(x => new[] { N(x.Parameter), N(x.PeakMagnitude), N(x.PeakFrequency), N(x.FirstMode), x.Error.Length == 0 ? "已计算" : x.Error }));
        }
        builder.Append("<h2>7. 复核清单</h2>");
        Table(builder, ["对象", "步骤", "负责人", "结果"], project.Reviews.Select(x => new[] { x.Target, x.Action, x.Owner, x.Result }));
        builder.Append("<h2>8. 备注与追溯</h2><p>" + H(project.Notes).Replace("\n", "<br>") + "</p>");
        Table(builder, ["UTC", "操作", "详情"], project.Journal.TakeLast(30).Select(x => new[] { x.Time.ToString("u"), x.Action, x.Detail }));
        builder.Append("<p class='limit'>真实座舱存在不规则几何、柔性结构和吸声边界。等效阻尼为用户假设；本结果需使用实际声源、几何和边界条件进行实测或更高保真仿真确认。不能由本报告给出实车声级或整改保证。</p></html>");
        return builder.ToString();
    }

    public static string ResponseSvg(Project project, AnalysisResult result)
    {
        var builder = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 900 280' role='img' aria-label='所有耳点共用参考的频率响应'>");
        builder.Append("<rect width='900' height='280' fill='#f4f3eb'/>");
        for (var db = 0; db >= -80; db -= 20)
        {
            var y = 30 - db * 2.25;
            builder.Append($"<path d='M60 {N(y)}H870' stroke='#c6d0c9'/><text x='12' y='{N(y + 5)}' font-size='13'>{db}</text>");
        }
        string[] colors = ["#216b72", "#c2673d", "#7174a1", "#708343", "#a15377", "#466595"];
        for (var i = 0; i < result.Curves.Count; i++)
        {
            var curve = result.Curves[i];
            var points = curve.Points.Select(point =>
            {
                var x = 60 + (point.Frequency - project.Model.MinimumFrequency) /
                    (project.Model.MaximumFrequency - project.Model.MinimumFrequency) * 810;
                var y = 30 - Math.Max(-80, point.RelativeDb(result.Reference)) * 2.25;
                return PointCsv.N(x) + "," + PointCsv.N(y);
            });
            builder.Append("<polyline fill='none' stroke='" + colors[i % colors.Length] + "' stroke-width='2' points='" + string.Join(" ", points) + "'/>");
            builder.Append($"<text x='{60 + (i % 6) * 135}' y='{245 + i / 6 * 20}' fill='{colors[i % colors.Length]}' font-size='12'>{H(curve.Name)}</text>");
        }
        builder.Append($"<text x='60' y='230' font-size='12'>{N(project.Model.MinimumFrequency)} Hz</text><text x='800' y='230' font-size='12'>{N(project.Model.MaximumFrequency)} Hz</text><text x='10' y='18' font-size='12'>相对dB</text></svg>");
        return builder.ToString();
    }

    private static StringBuilder Metadata(Project project, string metric)
    {
        var builder = new StringBuilder();
        builder.AppendLine(PointCsv.Join("#软件", Software.Name, Software.Version, "schema=" + project.Schema));
        builder.AppendLine(PointCsv.Join("#项目", project.Name, project.Id.ToString(), project.Revision));
        builder.AppendLine(PointCsv.Join("#来源", project.DataSource, project.IsDemo ? "模拟示例" : "人工模型"));
        builder.AppendLine(PointCsv.Join("#口径", metric, Software.Scope));
        builder.AppendLine(PointCsv.Join("#模型", "Lx_m=" + PointCsv.N(project.Model.Length), "Ly_m=" + PointCsv.N(project.Model.Width),
            "Lz_m=" + PointCsv.N(project.Model.Height), "c_m_s=" + PointCsv.N(project.Model.SoundSpeed), "zeta=" + PointCsv.N(project.Model.Damping),
            "cutoff_Hz=" + PointCsv.N(project.Model.Cutoff)));
        return builder;
    }

    private static void Table(StringBuilder builder, string[] headers, IEnumerable<string[]> rows)
    {
        builder.Append("<table><thead><tr>");
        foreach (var header in headers)
            builder.Append("<th>" + H(header) + "</th>");
        builder.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            builder.Append("<tr>");
            foreach (var cell in row)
                builder.Append("<td>" + H(cell) + "</td>");
            builder.Append("</tr>");
        }
        builder.Append("</tbody></table>");
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static string N(double value) => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
