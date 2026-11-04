using System.Globalization;
using System.Net;
using System.Text;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class ExportService
{
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var value = new string(name.Where(x => !invalid.Contains(x) && !char.IsControl(x)).Take(70).ToArray());
        return string.IsNullOrWhiteSpace(value) ? "motor-frequency" : value;
    }

    public static string CsvCell(string? text)
    {
        text ??= "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string N(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public static string FrequencyCsv(Project project)
    {
        ProjectValidator.ThrowIfInvalid(project);
        var text = new StringBuilder();
        text.AppendLine("project_id,project_name,software_version,source,candidate_id,candidate_name,order,rpm,frequency_hz,formula,basis,input_sha256");
        var hash = ProjectCodec.Hash(ProjectCodec.Serialize(project));
        foreach (var row in FrequencyEngine.Rows(project, true))
        {
            text.AppendLine(string.Join(',',
                CsvCell(project.Id.ToString()),
                CsvCell(project.Name),
                CsvCell(Software.Version),
                CsvCell(project.Demonstration ? "模拟演示" : "人工参数"),
                CsvCell(row.CandidateId),
                CsvCell(row.Name),
                N(row.Order),
                N(row.Rpm),
                N(row.FrequencyHz),
                CsvCell(row.Formula),
                CsvCell(row.Basis),
                CsvCell(hash)));
        }
        return text.ToString();
    }

    public static string CrossingCsv(Project project)
    {
        var text = new StringBuilder("project_name,source,candidate,band,lower_rpm,upper_rpm,lower_hz,upper_hz,band_basis,statement\n");
        foreach (var row in FrequencyEngine.Crossings(project))
        {
            text.AppendLine(string.Join(',', CsvCell(project.Name), CsvCell(project.Demonstration ? "模拟演示" : "人工参数"), CsvCell(row.CandidateName), CsvCell(row.BandName), N(row.LowerRpm), N(row.UpperRpm), N(row.LowerHz), N(row.UpperHz), CsvCell(row.Basis), CsvCell("仅几何频率交叠，不代表共振或噪声幅值")));
        }
        return text.ToString();
    }

    public static string EvidenceCsv(Project project)
    {
        var text = new StringBuilder("spectrum,source,condition,reference,rpm,frequency_hz,amplitude,unit,candidate,expected_hz,difference_hz,tolerance_hz,statement\n");
        foreach (var spectrum in project.Spectra)
        {
            foreach (var match in FrequencyEngine.MatchPeaks(project, spectrum))
            {
                text.AppendLine(string.Join(',', CsvCell(spectrum.Name), CsvCell(Labels.Origin(spectrum.Origin)), CsvCell(spectrum.Condition), CsvCell(spectrum.Reference), N(match.Rpm), N(match.ObservedHz), N(match.Amplitude), CsvCell(match.Unit), CsvCell(match.CandidateName), match.ExpectedHz is double expected ? N(expected) : "", match.DifferenceHz is double difference ? N(difference) : "", N(project.Review.MatchToleranceHz), CsvCell(match.Statement)));
            }
        }
        return text.ToString();
    }

    public static string ComparisonCsv(Project project, Scenario scenario)
    {
        var text = new StringBuilder("project,snapshot,source,inspect_rpm,candidate,current_order,snapshot_order,current_hz,snapshot_hz,delta_hz,statement\n");
        foreach (var row in FrequencyEngine.Compare(project, scenario))
        {
            string Value(double? number) => number is double value ? N(value) : "";
            text.AppendLine(string.Join(',', CsvCell(project.Name), CsvCell(scenario.Name), CsvCell(scenario.Demonstration ? "快照为模拟演示" : "参数快照"), N(project.Motor.InspectRpm), CsvCell(row.Name), Value(row.CurrentOrder), Value(row.SnapshotOrder), Value(row.CurrentHz), Value(row.SnapshotHz), Value(row.DifferenceHz), CsvCell(row.Statement)));
        }
        return text.ToString();
    }

    public static string Html(Project project)
    {
        ProjectValidator.ThrowIfInvalid(project);
        var text = new StringBuilder();
        string H(string? value) => WebUtility.HtmlEncode(value ?? "");
        text.AppendLine("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>电机候选频率复核报告</title>");
        text.AppendLine("<style>body{font:15px/1.7 system-ui,sans-serif;max-width:1100px;margin:35px auto;padding:20px;color:#263c36}table{width:100%;border-collapse:collapse;margin:18px 0}td,th{border:1px solid #d5dbd6;padding:7px;text-align:left}h1{font-size:26px}.notice{padding:16px;background:#f5ecd4}svg{width:100%}small{overflow-wrap:anywhere}@media print{body{margin:0}tr{break-inside:avoid}}</style>");
        text.AppendLine($"<h1>{H(Software.Name)} {Software.Version}</h1><h2>{H(project.Name)}</h2>");
        text.AppendLine($"<p>负责人：{H(project.Owner)}；项目标识：{project.Id}；输出时间：{DateTimeOffset.UtcNow:O}</p>");
        text.AppendLine($"<p class=\"notice\">来源：{(project.Demonstration ? "模拟演示" : "人工参数及用户导入资料")}。{H(Software.Boundary)}</p>");
        text.AppendLine("<p>项目目的：" + H(project.Purpose) + "</p>");
        var m = project.Motor;
        text.AppendLine($"<h2>电机与计算口径</h2><p>{H(Labels.Topology(m.Topology))}；p={m.PolePairs} 极对，Q={m.Slots} 槽，K={m.CommutatorSegments} 片。无刷使用 p/Q，有刷使用 K；不适用参数仅保留在项目档案。</p>");
        text.AppendLine($"<p>转速 {N(m.MinimumRpm)}～{N(m.MaximumRpm)} r/min，步长 {N(m.StepRpm)} r/min，观察转速 {N(m.InspectRpm)} r/min，图表上限 {N(m.MaximumHz)} Hz。终点总是纳入扫描；交叠区间解析计算，不依赖步长。</p>");
        text.AppendLine(new ChartGeometry(m).Svg(project));
        text.AppendLine("<h2>候选频率与依据</h2><table><tr><th>候选</th><th>阶次</th><th>观察频率 Hz</th><th>公式与依据</th></tr>");
        foreach (var candidate in FrequencyEngine.Candidates(m, project.CustomOrders))
        {
            text.AppendLine($"<tr><td>{H(candidate.Name)}{(candidate.Visible ? "" : "（隐藏）")}</td><td>{N(candidate.Order)}</td><td>{N(FrequencyEngine.At(candidate.Order, m.InspectRpm))}</td><td>{H(candidate.Formula)}；{H(candidate.Basis)}</td></tr>");
        }
        text.AppendLine("</table><h2>结构敏感频段</h2><table><tr><th>频段</th><th>范围 Hz</th><th>来源与依据</th></tr>");
        foreach (var band in project.Bands)
        {
            text.AppendLine($"<tr><td>{H(band.Name)}{(band.Enabled ? "" : "（停用）")}</td><td>{N(band.LowerHz)}～{N(band.UpperHz)}</td><td>{H(Labels.Origin(band.Origin))}；{H(band.Basis)}</td></tr>");
        }
        text.AppendLine("</table><h2>交叠转速区间</h2><p>区间包含端点；仅表示频率落入关注范围，不证明实际共振。</p><table><tr><th>候选</th><th>频段</th><th>进入 r/min</th><th>离开 r/min</th></tr>");
        foreach (var crossing in FrequencyEngine.Crossings(project))
        {
            text.AppendLine($"<tr><td>{H(crossing.CandidateName)}</td><td>{H(crossing.BandName)}</td><td>{N(crossing.LowerRpm)}</td><td>{N(crossing.UpperRpm)}</td></tr>");
        }
        text.AppendLine("</table><h2>导入频谱与邻近核对</h2>");
        text.AppendLine($"<p>绝对容差 {N(project.Review.MatchToleranceHz)} Hz 为用户工程设置；每条频谱按幅值选前 {project.Review.MaximumPeaks} 个内部局部极大点，不插值、不找端点峰、不合并同频候选。</p>");
        foreach (var spectrum in project.Spectra)
        {
            text.AppendLine($"<h3>{H(spectrum.Name)}</h3><p>{H(Labels.Origin(spectrum.Origin))}；{N(spectrum.Rpm)} r/min；单位 {H(spectrum.Unit)}；参考量 {H(spectrum.Reference)}；工况 {H(spectrum.Condition)}；{spectrum.Points.Count} 点；输入哈希 {H(spectrum.InputSha256)}</p>");
            text.AppendLine("<table><tr><th>谱峰 Hz</th><th>幅值</th><th>邻近候选</th><th>频差 Hz</th></tr>");
            foreach (var match in FrequencyEngine.MatchPeaks(project, spectrum))
            {
                text.AppendLine($"<tr><td>{N(match.ObservedHz)}</td><td>{N(match.Amplitude)} {H(match.Unit)}</td><td>{H(match.CandidateName)}</td><td>{(match.DifferenceHz is double delta ? N(delta) : "—")}</td></tr>");
            }
            text.AppendLine("</table>");
        }
        text.AppendLine("<h2>方案快照比较</h2><p>差值为当前−快照；均统一使用当前观察转速，非声级改善量。</p>");
        foreach (var scenario in project.Scenarios)
        {
            text.AppendLine($"<h3>{H(scenario.Name)} · {(scenario.Demonstration ? "模拟演示" : "参数快照")}</h3><table><tr><th>候选</th><th>当前 Hz</th><th>快照 Hz</th><th>差值 Hz</th><th>解释</th></tr>");
            foreach (var row in FrequencyEngine.Compare(project, scenario))
            {
                string V(double? value) => value is double number ? N(number) : "—";
                text.AppendLine($"<tr><td>{H(row.Name)}</td><td>{V(row.CurrentHz)}</td><td>{V(row.SnapshotHz)}</td><td>{V(row.DifferenceHz)}</td><td>{H(row.Statement)}</td></tr>");
            }
            text.AppendLine("</table>");
        }
        text.AppendLine("<h2>复核提示</h2><ul>");
        foreach (var note in FrequencyEngine.QualityNotes(project))
        {
            text.AppendLine("<li>" + H(note) + "</li>");
        }
        text.AppendLine("</ul><p>用户复核备注：" + H(project.Review.Notes) + "</p>");
        text.AppendLine("<h2>操作追溯</h2><table><tr><th>UTC 时间</th><th>操作</th><th>详情</th></tr>");
        foreach (var item in project.History.TakeLast(100))
        {
            text.AppendLine($"<tr><td>{item.At:O}</td><td>{H(item.Action)}</td><td>{H(item.Detail)}</td></tr>");
        }
        text.AppendLine("</table><small>输入项目 SHA-256：" + ProjectCodec.Hash(ProjectCodec.Serialize(project)) + "。报告自包含，不访问外部数据。全部频点保存在配套 .motorproj 中。</small></html>");
        return text.ToString();
    }
}
