using Blower.Core.Domain;

namespace Blower.Core.Analysis;

public sealed record ReviewFinding(string Severity, string Object, string Finding, string Action);

public static class QualityReview
{
    public static IReadOnlyList<ReviewFinding> Inspect(Project project)
    {
        var findings = new List<ReviewFinding>();
        void Add(string severity, string obj, string finding, string action)
        {
            findings.Add(new(severity, obj, finding, action));
        }
        if (project.Origin == Origin.Simulation)
        {
            Add("来源", "项目", "当前为模拟设计数据", "只用于流程和公式核对，实际设计前替换来源与参数");
        }
        if (string.IsNullOrWhiteSpace(project.Owner))
        {
            Add("待补充", "项目", "负责人未填写", "选中项目，在右侧填写负责人并应用");
        }
        if (string.IsNullOrWhiteSpace(project.Vehicle))
        {
            Add("待补充", "项目", "车辆或平台为空", "补充平台及适用安装边界");
        }
        foreach (var mode in project.Modes.Where(x => x.Enabled))
        {
            if (mode.DampingRatio == null)
            {
                Add("未知", mode.Name, "阻尼比未知，阻尼半宽项未计入", "可保留未知；依据试验或仿真补充后重新计算");
            }
            if (mode.Origin != project.Origin)
            {
                Add("来源", mode.Name, "模态来源类别与项目不同", "核对模态来源；报告保留每个模态的独立标记");
            }
            if (string.IsNullOrWhiteSpace(mode.Location))
            {
                Add("待补充", mode.Name, "结构位置为空", "填写安装位置与可复现的模态边界条件");
            }
            if (mode.Source.Contains("待补充", StringComparison.Ordinal))
            {
                Add("待补充", mode.Name, "模态来源尚未完善", "补充文件编号、工况、安装状态及数据提供方");
            }
        }
        var enabled = project.Modes.Where(x => x.Enabled).OrderBy(x => x.FrequencyHz).ToList();
        for (var i = 1; i < enabled.Count; i++)
        {
            var a = Engine.Band(enabled[i - 1], project.Settings);
            var b = Engine.Band(enabled[i], project.Settings);
            if (a.UpperHz >= b.LowerHz)
            {
                Add("注意", a.Name + " / " + b.Name, "模态敏感区重叠", "阻尼带宽近似可能不适用；组合计数按模态对象分别计算，不代表独立能量");
            }
        }
        foreach (var plan in project.Plans)
        {
            if (plan.Gears.Count == 0)
            {
                Add("待补充", plan.Name, "没有挡位", "选中方案后添加挡位或导入CSV");
            }
            if (plan.Gears.Count > 0 && plan.Gears.All(x => x.Fixed))
            {
                Add("注意", plan.Name, "全部挡位固定", "扫描仅核对原始点，不会产生转速调整");
            }
        }
        foreach (var task in project.Checks.Where(x => x.State == CheckState.Recheck))
        {
            Add("需复核", task.Title, "输入变化后原验证证据需要复核", "在验证清单选中任务，核对新输入后更新证据与状态");
        }
        if (project.Settings.AbsoluteBandHz == 0 && project.Settings.RelativeBandPercent == 0)
        {
            Add("注意", "规则", "绝对和百分比敏感半宽均为零", "确认是否只使用阻尼或附加半宽，避免漏掉参数不确定性");
        }
        return findings;
    }
}
