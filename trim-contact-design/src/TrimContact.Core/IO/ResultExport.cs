using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;

namespace TrimContact.Core.IO;

public static class ResultExport
{
    private static string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string Row(params string[] cells) => string.Join(",", cells.Select(Cell));
    private static string H(string value) => WebUtility.HtmlEncode(value);

    public static void RequireCurrent(DesignProject project, AnalysisReport report)
    {
        if (report.InputHash != ProjectCodec.InputHash(project))
            throw new InvalidOperationException("输入已变化，必须重新计算后导出结果");
    }

    public static string Json(DesignProject project, AnalysisReport report)
    {
        RequireCurrent(project, report);
        return JsonSerializer.Serialize(new
        {
            software = Product.Name,
            version = Product.Version,
            boundary = Product.Boundary,
            project,
            report
        }, ProjectCodec.Options);
    }

    public static string Csv(DesignProject project, AnalysisReport report)
    {
        RequireCurrent(project, report);
        var text = new StringBuilder();
        text.AppendLine(Row("软件", Product.Name, Product.Version, report.Method));
        text.AppendLine(Row("输入SHA256", report.InputHash, "来源", project.Origin.Label(), project.Source));
        text.AppendLine(Row("限制", Product.Boundary, "采样", report.Phases.ToString(CultureInfo.InvariantCulture),
            "公差角点不是概率"));
        text.AppendLine(Row("方案", "点位ID", "点位", "类型", "状态", "最小压缩mm", "最大压缩mm",
            "最小法向力N", "最大法向力N", "最大切向力绝对值N", "最大滑移余量mm", "最大限值比",
            "分离采样数", "滑移采样数", "总采样数", "压缩控制工况", "滑移控制工况", "限值控制工况"));
        foreach (var variant in report.Variants)
            foreach (var point in variant.Contacts.Concat(variant.Clips))
                text.AppendLine(Row(variant.Name, point.PointId.ToString(), point.Name, point.Kind, point.State,
                    N(point.MinCompressionMm), N(point.MaxCompressionMm), N(point.MinNormalN), N(point.MaxNormalN),
                    N(point.MaxAbsShearN), N(point.MaxSlipMm), N(point.MaxUtilization), point.DetachedSamples.ToString(),
                    point.SlidingSamples.ToString(), point.TotalSamples.ToString(), point.CompressionCase,
                    point.SlipCase, point.GoverningCase));
        return text.ToString();
    }

    public static string TraceCsv(DesignProject project, AnalysisReport report)
    {
        RequireCurrent(project, report);
        var text = new StringBuilder();
        text.AppendLine(Row("输入SHA256", report.InputHash, "来源", project.Origin.Label(), "仅名义公差逐相位"));
        text.AppendLine(Row("方案", "载荷", "相位deg", "类型", "点位ID", "点位", "压缩mm", "法向力N",
            "切向力N", "滑移余量mm", "限值比", "饰板Zmm", "切向位移mm", "平衡残差N"));
        foreach (var variant in report.Variants)
            foreach (var trace in variant.NominalTraces)
                foreach (var state in trace.Samples)
                    foreach (var item in state.Contacts.Select(p => (p, kind: "接触点"))
                        .Concat(state.Clips.Select(p => (p, kind: "卡扣"))))
                        text.AppendLine(Row(variant.Name, trace.LoadName, N(state.PhaseDegrees), item.kind,
                            item.p.Id.ToString(), item.p.Name, N(item.p.CompressionMm), N(item.p.NormalN),
                            N(item.p.ShearN), N(item.p.SlipMm), N(item.p.Utilization), N(state.Displacement.Z),
                            N(state.TangentialMm), N(state.ResidualN)));
        return text.ToString();
    }

    public static string TasksCsv(DesignProject project)
    {
        var text = new StringBuilder();
        text.AppendLine(Row("软件", Product.Name, Product.Version, "来源", project.Origin.Label(), project.Source));
        text.AppendLine(Row("任务ID", "关联点位ID", "标题", "方法", "判据", "负责人", "完成", "证据"));
        foreach (var t in project.Tasks)
            text.AppendLine(Row(t.Id.ToString(), t.PointId?.ToString() ?? "", t.Title, t.Method,
                t.Acceptance, t.Owner, t.Completed ? "是（用户记录）" : "否", t.Evidence));
        return text.ToString();
    }

    public static string SweepCsv(DesignProject project, string parameter, IReadOnlyList<SweepRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(Row("软件", Product.Name, Product.Version, "输入SHA256", ProjectCodec.InputHash(project)));
        text.AppendLine(Row("来源", project.Origin.Label(), project.Source, "扫描参数", parameter));
        text.AppendLine(Row("参数值", "存在分离点数", "存在滑移点数", "超限点数", "最大卡扣限值比", "最大压缩mm", "最大滑移余量mm"));
        foreach (var r in rows)
            text.AppendLine(Row(N(r.Value), r.SeparationPoints.ToString(), r.SlidingPoints.ToString(),
                r.OverLimitPoints.ToString(), N(r.MaxClipUtilization), N(r.MaxCompressionMm), N(r.MaxSlipMm)));
        return text.ToString();
    }

    public static string Html(DesignProject project, AnalysisReport report)
    {
        RequireCurrent(project, report);
        var html = new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>接触设计报告</title><style>body{font-family:system-ui,sans-serif;max-width:1200px;margin:40px auto;color:#183239}table{border-collapse:collapse;width:100%;font-size:14px}td,th{border:1px solid #afc4c7;padding:8px;text-align:left}pre{white-space:pre-wrap;overflow-wrap:anywhere}h2{border-bottom:2px solid #317c80;padding-bottom:8px}.note{background:#fff0d0;padding:16px} @media print{body{margin:10mm}tr{break-inside:avoid}}</style><body>");
        html.Append($"<h1>{H(Product.Name)} {Product.Version}</h1><p>{H(project.Name)} / 修订{H(project.Revision)}</p>");
        html.Append($"<p>来源：{H(project.Origin.Label())}；{H(project.Source)}</p><p class='note'>{H(Product.Boundary)}</p>");
        html.Append($"<p>方法{report.Method}；计算UTC {report.CalculatedUtc:O}；{report.Phases}相位；公差角点{report.ToleranceEnabled}。角点采样比例不是概率。</p><pre>输入SHA256 {report.InputHash}</pre>");
        foreach (var variant in report.Variants)
        {
            html.Append($"<h2>{H(variant.Name)}</h2><p>总采样{variant.EvaluatedStates}；公差条件{variant.ToleranceCases}；最大平衡残差{N(variant.MaxResidualN)} N</p>");
            html.Append("<table><tr><th>点位</th><th>类型/状态</th><th>压缩范围mm</th><th>法向力范围N</th><th>最大滑移余量mm</th><th>最大限值比</th><th>控制工况</th></tr>");
            foreach (var point in variant.Contacts.Concat(variant.Clips))
                html.Append($"<tr><td>{H(point.Name)}</td><td>{H(point.Kind)} / {H(point.State)}</td><td>{N(point.MinCompressionMm)} ～ {N(point.MaxCompressionMm)}</td><td>{N(point.MinNormalN)} ～ {N(point.MaxNormalN)}</td><td>{N(point.MaxSlipMm)}</td><td>{N(point.MaxUtilization)}</td><td>{H(point.GoverningCase)}<br>{H(point.CompressionCase)}<br>{H(point.SlipCase)}</td></tr>");
            html.Append("</table>");
        }
        html.Append("<h2>验证清单（完成状态由用户记录）</h2><table><tr><th>任务</th><th>方法</th><th>判据</th><th>状态与证据</th></tr>");
        foreach (var task in project.Tasks)
            html.Append($"<tr><td>{H(task.Title)}</td><td>{H(task.Method)}</td><td>{H(task.Acceptance)}</td><td>{(task.Completed ? "用户标记完成" : "待验证")}；{H(task.Owner)}；{H(task.Evidence)}</td></tr>");
        html.Append("</table><h2>全部设计输入</h2><pre>");
        html.Append(H(ProjectCodec.Serialize(project)));
        html.Append("</pre></body></html>");
        return html.ToString();
    }
}
