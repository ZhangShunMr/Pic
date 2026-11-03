using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public sealed class QualityInspector
{
    public List<QualityIssue> Inspect(AcousticProject project, List<PairResult> results)
    {
        var issues = new List<QualityIssue>();
        var pairedIds = project.Pairs.SelectMany(pair => new[] { pair.BaselineId, pair.TargetId }).ToHashSet();
        var comparability = new Comparability();
        foreach (var condition in project.Conditions)
        {
            foreach (var missing in comparability.MissingFields(condition, project.Settings))
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "condition.incomplete", "condition", condition.Id,
                    condition.Name, missing, "打开工况，按原始试验记录补充该字段后应用；不要用零代替未知"));
            }
        }

        foreach (var trial in project.Trials)
        {
            if (trial.ValidBandCount == 0)
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "trial.empty", "trial", trial.Id,
                    trial.Name, "没有有效频带", "打开试验频带表，录入声级或通过CSV预检导入"));
            }

            if (trial.MissingBandCount > 0)
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "trial.missing", "trial", trial.Id,
                    trial.Name, $"存在{trial.MissingBandCount}个明确缺失频带", "核对仪器导出记录；确实缺失时保留空值，不补零"));
            }

            if (!pairedIds.Contains(trial.Id))
            {
                issues.Add(new QualityIssue(IssueSeverity.Information, "trial.unpaired", "trial", trial.Id,
                    trial.Name, "尚未被显式配对引用", "按试验计划建立基线与目标对应关系，或说明保留为参考数据"));
            }

            if (trial.Source != SourceKind.Measured)
            {
                issues.Add(new QualityIssue(IssueSeverity.Information, "trial.source", "trial", trial.Id,
                    trial.Name, "数据来源为" + trial.Source.Text(), "输出时继续保留来源标识；本记录不能证明真实车辆应用效果"));
            }
        }

        foreach (var result in results)
        {
            foreach (var reason in result.BlockingReasons)
            {
                issues.Add(new QualityIssue(IssueSeverity.Error, "pair.blocked", "pair", result.PairId,
                    result.Title, reason, "打开配对核对基线与目标，再进入对应工况或试验按真实记录修正"));
            }

            foreach (var warning in result.Warnings)
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "pair.warning", "pair", result.PairId,
                    result.Title, warning, "展开配对来源和时间详情，确认输入或保留说明"));
            }

            if (result.Screening?.Status == ResultStatus.Alert)
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "screening.alert", "pair", result.PairId,
                    result.Title, "最大频带绝对差超过用户" + result.Purpose.Text() + "阈值",
                    "检查封堵恢复状态、仪器标定、声源稳定性和环境条件，再决定是否补测；不要仅改阈值消除警示"));
            }
        }

        foreach (var purpose in new[] { PairPurpose.Drift, PairPurpose.Recovery })
        {
            if (project.Pairs.All(pair => pair.Purpose != purpose))
            {
                issues.Add(new QualityIssue(IssueSeverity.Warning, "coverage." + purpose, "project", project.Id,
                    project.Name, "尚无" + purpose.Text() + "配对", "先采集对应试验，再明确指定基线和目标；不能推断稳定或已恢复"));
            }
        }

        return issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Title, StringComparer.Ordinal).ToList();
    }
}
