using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.Core.Operations;

public sealed record ProposedPair(TrialPair Pair, string Baseline, string Target, List<string> Reasons, bool Duplicate);
public sealed record PairingPreview(string ProjectHash, List<ProposedPair> Rows)
{
    public bool CanApply => Rows.Count > 0 && Rows.All(row => !row.Duplicate);
}

public sealed class BatchPairing
{
    public PairingPreview Preview(AcousticProject project, Guid baselineId, IReadOnlyList<Guid> targetIds, PairPurpose purpose)
    {
        var baseline = project.Trials.FirstOrDefault(trial => trial.Id == baselineId && trial.Kind == TrialKind.Baseline)
            ?? throw new DomainValidationException("baseline", "先明确选择一个基线试验");
        if (targetIds.Count == 0 || targetIds.Distinct().Count() != targetIds.Count)
        {
            throw new DomainValidationException("targets", "目标列表不能为空或重复");
        }

        var rows = new List<ProposedPair>();
        var validator = new ProjectValidator();
        var comparator = new Comparability();
        foreach (var id in targetIds)
        {
            var target = project.Trials.FirstOrDefault(trial => trial.Id == id)
                ?? throw new DomainValidationException("target", "目标试验不存在");
            var pair = new TrialPair
            {
                BaselineId = baseline.Id,
                TargetId = id,
                Purpose = purpose,
                Notes = "用户显式勾选目标并确认批量配对；共享基线时不作为独立配对估计区间"
            };
            var duplicate = project.Pairs.Any(item => item.BaselineId == pair.BaselineId && item.TargetId == pair.TargetId && item.Purpose == pair.Purpose);
            var candidate = ProjectCodec.Clone(project);
            candidate.Pairs.Add(pair);
            var structural = validator.Inspect(candidate).Where(error => error.Path.StartsWith("pairs", StringComparison.Ordinal)).ToList();
            if (structural.Count > 0 && !duplicate)
            {
                throw new DomainValidationException(structural);
            }

            var left = project.Conditions.Single(condition => condition.Id == baseline.ConditionId);
            var right = project.Conditions.Single(condition => condition.Id == target.ConditionId);
            var reasons = comparator.Inspect(left, right, project.Settings);
            if (baseline.Source != target.Source)
            {
                reasons.Add("来源类别不一致");
            }

            rows.Add(new ProposedPair(pair, baseline.Name, target.Name, reasons, duplicate));
        }

        return new PairingPreview(ProjectCodec.Fingerprint(project), rows);
    }

    public void Apply(ProjectSession session, PairingPreview preview)
    {
        if (!preview.CanApply || ProjectCodec.Fingerprint(session.Project) != preview.ProjectHash)
        {
            throw new DomainValidationException("pairing", "预检重复、为空或已过期，请重新选择目标预检");
        }

        session.Apply("用户确认批量配对", session.Project.Id.ToString(), project =>
            project.Pairs.AddRange(preview.Rows.Select(row => ProjectCodec.Clone(row.Pair))));
    }
}
