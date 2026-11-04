using System.Net;
using System.Text;
using Blower.Core.Analysis;
using Blower.Core.Domain;

namespace Blower.Core.IO;

public static class Exports
{
    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string N(double value) => CsvExchange.Number(value);

    private static void RequireCurrent(Project project, AnalysisResult result)
    {
        if (result.InputHash != ProjectCodec.InputHash(project))
        {
            throw new InvalidOperationException("结果已过期；重新计算后导出");
        }
    }

    public static string Contacts(Project project, AnalysisResult result)
    {
        RequireCurrent(project, result);
        var b = new StringBuilder();
        b.AppendLine(CsvExchange.Row("software", "version", "project", "source", "input_sha256", "plan", "gear", "rpm",
            "order", "effective_order", "frequency_hz", "lower_hz", "upper_hz", "mode", "mode_hz", "mode_source", "half_width_hz", "clearance_hz", "status"));
        foreach (var c in result.Contacts)
        {
            b.AppendLine(CsvExchange.Row(Software.Name, Software.Version, project.Name,
                Labels.Source(project.Origin) + " / " + project.Source, result.InputHash,
                c.Plan, c.Gear, N(c.Rpm), c.Order, N(c.OrderNumber), N(c.FrequencyHz), N(c.LowerHz), N(c.UpperHz),
                c.Mode, N(c.ModeHz), Labels.Source(project.Modes.Single(x => x.Id == c.ModeId).Origin)
                    + " / " + project.Modes.Single(x => x.Id == c.ModeId).Source,
                N(c.HalfWidthHz), N(c.ClearanceHz), c.Status));
        }
        return b.ToString();
    }

    public static string Crossings(Project project, AnalysisResult result)
    {
        RequireCurrent(project, result);
        var b = new StringBuilder();
        b.AppendLine(CsvExchange.Row("project", "source", "input_sha256", "mode", "order", "effective_order", "exact_rpm", "lower_rpm", "upper_rpm", "within_speed_range"));
        foreach (var c in result.Crossings)
        {
            b.AppendLine(CsvExchange.Row(project.Name, Labels.Source(project.Origin), result.InputHash,
                c.Mode, c.Order, N(c.EffectiveOrder), N(c.ExactRpm), N(c.LowerRpm), N(c.UpperRpm), c.InRange.ToString()));
        }
        return b.ToString();
    }

    public static string Checks(Project project)
    {
        var b = new StringBuilder();
        b.AppendLine(CsvExchange.Row("project", "source", "plan", "title", "method", "owner", "state", "evidence", "updated_utc"));
        foreach (var task in project.Checks)
        {
            b.AppendLine(CsvExchange.Row(project.Name, Labels.Source(project.Origin),
                project.Plans.Single(x => x.Id == task.PlanId).Name, task.Title, task.Method,
                task.Owner, Labels.State(task.State), task.Evidence, task.Updated.ToString("O")));
        }
        return b.ToString();
    }

    public static string GearTemplate(SpeedPlan? plan)
    {
        var b = new StringBuilder("name,rpm,fixed\n");
        if (plan != null)
        {
            foreach (var gear in plan.Gears)
            {
                b.AppendLine(CsvExchange.Row(gear.Name, N(gear.Rpm), gear.Fixed.ToString().ToLowerInvariant()));
            }
        }
        return b.ToString();
    }

    public static string ModeTemplate(Project project)
    {
        var b = new StringBuilder("name,frequency_hz,damping_ratio,source\n");
        foreach (var mode in project.Modes)
        {
            b.AppendLine(CsvExchange.Row(mode.Name, N(mode.FrequencyHz),
                mode.DampingRatio.HasValue ? N(mode.DampingRatio.Value) : "", mode.Source));
        }
        return b.ToString();
    }

    public static string Report(Project project, AnalysisResult result, string planId)
    {
        RequireCurrent(project, result);
        var b = new StringBuilder();
        b.Append("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>鼓风机设计报告</title>");
        b.Append("<style>body{font:15px/1.7 sans-serif;color:#303238;max-width:1200px;margin:40px auto;padding:20px}h1{font-size:26px}h2{border-bottom:1px solid #bbb;padding-bottom:8px}table{width:100%;border-collapse:collapse;font-size:12px}td,th{border:1px solid #ccc;padding:7px;text-align:left}th{background:#f2f0e9}.note{padding:16px;background:#fff0db}svg{width:100%}pre{white-space:pre-wrap;overflow-wrap:anywhere}section{margin:28px 0}@media print{body{margin:0}tr{break-inside:avoid}}</style>");
        b.Append($"<h1>{E(Software.Name)} {Software.Version}</h1><h2>{E(project.Name)}</h2>");
        b.Append($"<p>修订 {project.Revision} · 项目 {E(project.Id)} · 负责人 {E(project.Owner)} · 平台 {E(project.Vehicle)}</p>");
        b.Append($"<p class=\"note\">{E(Labels.Source(project.Origin))}：{E(project.Source)}<br>{E(Software.Boundary)}</p>");
        b.Append($"<p>生成时间 {DateTimeOffset.UtcNow:O} · 输入SHA256 {result.InputHash}</p><p>{E(project.Notes)}</p>");
        var s = project.Settings;
        b.Append("<section><h2>计算口径与输入</h2>");
        b.Append($"<p>叶片数{s.Blades}；范围{N(s.MinRpm)}～{N(s.MaxRpm)}rpm；转速容差±{N(s.SpeedToleranceRpm)}rpm；挡位最小间距{N(s.MinimumGapRpm)}rpm。</p>");
        b.Append($"<p>扫描窗口±{N(s.ScanWindowRpm)}rpm；步长{N(s.ScanStepRpm)}rpm；偏移权重{N(s.ShiftWeight)}。扫描代价=接近组合数+权重×|转速偏移|/max(1,窗口)，固定挡位保持原转速。</p>");
        b.Append($"<p>敏感半宽=max({N(s.AbsoluteBandHz)}Hz,模态频率×{N(s.RelativeBandPercent)}%,可选ζfn,单模态附加半宽)。阻尼项{(s.UseDamping ? "启用" : "停用")}；阻尼未知时不补值。该半宽为工程筛查规则，不是标准限值。</p>");
        b.Append("<p>f=q×n/60；叶片阶次q=叶片数×谐波倍数；精确交叉n=60fn/q。频率区间计入转速容差，与模态敏感区间相交（含边界）为潜在接近。净距为两区间间隔，负值表示重叠深度；不代表幅值。所有方案共用同一组模态、阶次和规则；比较按挡位序号。</p>");
        b.Append("<table><tr><th>模态</th><th>位置</th><th>Hz</th><th>阻尼比</th><th>启用</th><th>来源</th></tr>");
        foreach (var mode in project.Modes)
        {
            b.Append($"<tr><td>{E(mode.Name)}</td><td>{E(mode.Location)}</td><td>{N(mode.FrequencyHz)}</td><td>{(mode.DampingRatio.HasValue ? N(mode.DampingRatio.Value) : "未知")}</td><td>{mode.Enabled}</td><td>{E(Labels.Source(mode.Origin))} / {E(mode.Source)}</td></tr>");
        }
        b.Append("</table><table><tr><th>阶次</th><th>类型</th><th>有效阶次</th><th>启用</th><th>依据</th></tr>");
        foreach (var order in project.Orders)
        {
            b.Append($"<tr><td>{E(order.Name)}</td><td>{E(Labels.Kind(order.Kind))}</td><td>{N(order.EffectiveOrder(s.Blades))}</td><td>{order.Enabled}</td><td>{E(order.Evidence)}</td></tr>");
        }
        b.Append("</table></section><section><h2>转速—频率图</h2>");
        b.Append(ChartModel.Create(project, result, planId).ToSvg(project.Plans.Single(x => x.Id == planId).Name, Labels.Source(project.Origin)));
        b.Append("<p>彩色斜线为设计阶次；紫色横带为敏感区；红色挡位线表示存在接近组合，绿色表示当前规则内未接近。不是测量频谱。</p></section>");
        b.Append("<section><h2>方案汇总</h2><table><tr><th>方案</th><th>挡位数</th><th>接近挡位</th><th>接近组合</th><th>最小净距Hz</th></tr>");
        foreach (var summary in result.Summaries)
        {
            b.Append($"<tr><td>{E(summary.Name)}</td><td>{summary.GearCount}</td><td>{summary.NearGearCount}</td><td>{summary.ContactCount}</td><td>{summary.MinimumClearanceHz:0.###}</td></tr>");
        }
        b.Append("</table>");
        foreach (var plan in project.Plans)
        {
            b.Append($"<h3>{E(plan.Name)}</h3><p>{E(plan.Notes)}</p><table><tr><th>挡位</th><th>rpm</th><th>固定</th><th>备注</th></tr>");
            foreach (var gear in plan.Gears)
            {
                b.Append($"<tr><td>{E(gear.Name)}</td><td>{N(gear.Rpm)}</td><td>{gear.Fixed}</td><td>{E(gear.Notes)}</td></tr>");
            }
            b.Append("</table>");
        }
        b.Append("</section><section><h2>敏感范围依据</h2><table><tr><th>模态</th><th>区间Hz</th><th>依据</th></tr>");
        foreach (var band in result.Bands)
        {
            b.Append($"<tr><td>{E(band.Name)}</td><td>{N(band.LowerHz)}～{N(band.UpperHz)}</td><td>{E(band.Basis)}</td></tr>");
        }
        b.Append("</table></section><section><h2>潜在接近明细</h2><table><tr><th>方案/挡位</th><th>阶次</th><th>中心频率Hz</th><th>容差区间Hz</th><th>模态</th><th>净距Hz</th></tr>");
        foreach (var c in result.Contacts.Where(x => x.Near))
        {
            b.Append($"<tr><td>{E(c.Plan)} / {E(c.Gear)}</td><td>{E(c.Order)}</td><td>{N(c.FrequencyHz)}</td><td>{N(c.LowerHz)}～{N(c.UpperHz)}</td><td>{E(c.Mode)}</td><td>{N(c.ClearanceHz)}</td></tr>");
        }
        b.Append("</table></section><section><h2>交叉转速</h2><table><tr><th>模态</th><th>阶次</th><th>精确交叉rpm</th><th>敏感转速区rpm</th><th>与设定范围相交</th></tr>");
        foreach (var crossing in result.Crossings)
        {
            b.Append($"<tr><td>{E(crossing.Mode)}</td><td>{E(crossing.Order)}</td><td>{N(crossing.ExactRpm)}</td><td>{N(crossing.LowerRpm)}～{N(crossing.UpperRpm)}</td><td>{crossing.InRange}</td></tr>");
        }
        b.Append("</table></section><section><h2>验证清单</h2><table><tr><th>任务</th><th>方法</th><th>负责人/状态</th><th>证据</th></tr>");
        foreach (var task in project.Checks)
        {
            b.Append($"<tr><td>{E(task.Title)}</td><td>{E(task.Method)}</td><td>{E(task.Owner)} / {E(Labels.State(task.State))}</td><td>{E(task.Evidence)}</td></tr>");
        }
        b.Append("</table></section><section><h2>审计记录</h2><table><tr><th>UTC</th><th>修订</th><th>操作</th><th>说明</th></tr>");
        foreach (var item in project.Audit)
        {
            b.Append($"<tr><td>{item.Time:O}</td><td>{item.Revision}</td><td>{E(item.Action)}</td><td>{E(item.Detail)}</td></tr>");
        }
        b.Append("</table></section><section><h2>适用边界</h2><p>未处理波形、实测阶次幅值、风量、效率和声压。阻尼半宽近似仅适于轻阻尼且相互分离的模态。结构条件、温度、边界与来源应由工程师复核。完成验证记录仅代表用户填写，软件不认证证据。扫描网格不覆盖连续最优解，必须验证扫描方案的风量、效率及整车表现。</p></section></html>");
        return b.ToString();
    }
}
