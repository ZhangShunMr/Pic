using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public sealed class AnalysisEngine
{
    private readonly ProjectValidator validator = new();
    private readonly Comparability comparability = new();

    public AnalysisReport Calculate(AcousticProject project)
    {
        validator.Ensure(project);
        var conditions = project.Conditions.ToDictionary(condition => condition.Id);
        var trials = project.Trials.ToDictionary(trial => trial.Id);
        var pairs = project.Pairs.Select(pair => Compare(pair, trials, conditions, project.Settings)).ToList();
        return new AnalysisReport
        {
            ProjectId = project.Id,
            Revision = project.Revision,
            Pairs = pairs,
            Effects = Summarize(pairs, trials, conditions, project.Settings),
            Repeatability = Repeatability(project, conditions),
            Coverage = BuildCoverage(project, pairs),
            Issues = new QualityInspector().Inspect(project, pairs)
        };
    }

    private PairResult Compare(
        TrialPair pair,
        Dictionary<Guid, AcousticTrial> trials,
        Dictionary<Guid, MeasurementCondition> conditions,
        AnalysisSettings settings)
    {
        var baseline = trials[pair.BaselineId];
        var target = trials[pair.TargetId];
        var left = conditions[baseline.ConditionId];
        var right = conditions[target.ConditionId];
        var reasons = comparability.Inspect(left, right, settings);
        var warnings = new List<string>();
        if (baseline.Source != target.Source)
        {
            reasons.Add("来源类别不同，禁止混合模拟、实测、人工或估算作为同一配对");
        }

        if (baseline.Source != SourceKind.Measured)
        {
            warnings.Add("包含" + baseline.Source.Text() + "数据，不能证明真实应用效果");
        }

        var elapsed = (target.Timestamp - baseline.Timestamp).TotalSeconds;
        if (elapsed < 0)
        {
            warnings.Add("目标时间早于基线，核对时间与配对用途");
        }

        var frequencies = Frequencies([baseline, target]);
        var bands = new List<BandDifference>();
        foreach (var frequency in frequencies)
        {
            var before = baseline.LevelAt(frequency);
            var after = target.LevelAt(frequency);
            var status = reasons.Count > 0 ? ResultStatus.Blocked :
                !before.HasValue || !after.HasValue ? ResultStatus.Missing : ResultStatus.Valid;
            var difference = status == ResultStatus.Valid ? before - after : null;
            var interpretation = difference switch
            {
                null => status.Text(),
                > 0 => "目标声级低于基线",
                < 0 => "目标声级高于基线",
                _ => "两者声级相同"
            };
            if (difference.HasValue && Math.Abs(difference.Value) < settings.MinimumEffectDisplay)
            {
                interpretation += "；低于用户变化标记阈值";
            }

            bands.Add(new BandDifference
            {
                Frequency = frequency,
                Baseline = before,
                Target = after,
                Difference = difference,
                Status = status,
                Interpretation = interpretation
            });
        }

        if (bands.Any(band => band.Status == ResultStatus.Missing))
        {
            warnings.Add("部分频带缺失，仅计算共同有效频带，不补零");
        }

        var validCount = bands.Count(band => band.Difference.HasValue);
        var baseTotal = Total(baseline, left);
        var targetTotal = Total(target, right);
        var sameFrequencies = baseline.Bands.Select(band => band.Frequency).Order().SequenceEqual(
            target.Bands.Select(band => band.Frequency).Order());
        var totalNote = "只表示已录入频带范围的能量和，不是全频总声级";
        double? totalDifference = null;
        if (reasons.Count > 0)
        {
            totalNote = "条件或来源不可比，不比较合成声级";
        }
        else if (!sameFrequencies)
        {
            totalNote = "频带集合不同，不比较合成声级";
        }
        else if (!left.NonOverlappingBands || !right.NonOverlappingBands)
        {
            totalNote = "未声明频带互不重叠，不计算合成声级";
        }
        else if (!baseTotal.HasValue || !targetTotal.HasValue)
        {
            totalNote = "频带缺失或为空，不计算合成声级";
        }
        else
        {
            totalDifference = baseTotal - targetTotal;
        }

        ScreeningResult? screening = null;
        if (pair.Purpose != PairPurpose.Effect)
        {
            var threshold = pair.Purpose == PairPurpose.Drift ? settings.DriftThreshold : settings.RecoveryThreshold;
            var worst = bands.Where(band => band.Difference.HasValue)
                .OrderByDescending(band => Math.Abs(band.Difference!.Value)).FirstOrDefault();
            var maximum = worst is null ? (double?)null : Math.Abs(worst.Difference!.Value);
            screening = new ScreeningResult(
                threshold,
                maximum,
                worst?.Frequency,
                maximum is null ? ResultStatus.Empty : maximum > threshold ? ResultStatus.Alert : ResultStatus.Within);
        }

        return new PairResult
        {
            PairId = pair.Id,
            BaselineId = baseline.Id,
            TargetId = target.Id,
            BaselineName = baseline.Name,
            TargetName = target.Name,
            Purpose = pair.Purpose,
            TargetKind = target.Kind,
            Source = baseline.Source,
            LocationIds = [.. target.LocationIds],
            Status = reasons.Count > 0 ? ResultStatus.Blocked : validCount > 0 ? ResultStatus.Valid : ResultStatus.Empty,
            BlockingReasons = reasons,
            Warnings = warnings,
            Bands = bands,
            BaselineTotal = baseTotal,
            TargetTotal = targetTotal,
            TotalDifference = totalDifference,
            TotalNote = totalNote,
            ElapsedSeconds = elapsed,
            Screening = screening,
            BaselineSource = baseline.SourceNote,
            TargetSource = target.SourceNote
        };
    }

    private List<EffectSummary> Summarize(
        List<PairResult> pairs,
        Dictionary<Guid, AcousticTrial> trials,
        Dictionary<Guid, MeasurementCondition> conditions,
        AnalysisSettings settings)
    {
        var groups = pairs.Where(pair => pair.Purpose == PairPurpose.Effect && pair.Status == ResultStatus.Valid)
            .GroupBy(pair => Key(pair.LocationIds) + "|" + pair.Source);
        var summaries = new List<EffectSummary>();
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var reasons = new List<string>();
            // Check all pairs, rather than only one anchor, so tolerances do not
            // silently accumulate through a nontransitive matching chain.
            for (var index = 0; index < entries.Count; index++)
            {
                for (var other = index + 1; other < entries.Count; other++)
                {
                    reasons.AddRange(comparability.Inspect(
                        conditions[trials[entries[index].BaselineId].ConditionId],
                        conditions[trials[entries[other].BaselineId].ConditionId], settings));
                }
            }

            var ids = entries.SelectMany(pair => new[] { pair.BaselineId, pair.TargetId }).ToList();
            var independent = ids.Distinct().Count() == ids.Count;
            var bands = new List<EffectBandSummary>();
            foreach (var frequency in entries.SelectMany(pair => pair.Bands.Select(band => band.Frequency)).Distinct().Order())
            {
                var valid = entries.SelectMany(pair => pair.Bands)
                    .Where(band => band.Frequency == frequency && band.Difference.HasValue).ToList();
                var statistics = reasons.Count > 0
                    ? new PairedStatistics { Count = valid.Count, IntervalNote = "跨配对工况不同，禁止汇总" }
                    : AcousticMath.Paired(
                        valid.Select(band => band.Baseline!.Value).ToList(),
                        valid.Select(band => band.Target!.Value).ToList(), independent);
                bands.Add(new EffectBandSummary { Frequency = frequency, Statistics = statistics });
            }

            summaries.Add(new EffectSummary
            {
                Key = group.Key,
                LocationIds = [.. entries[0].LocationIds],
                Source = entries[0].Source,
                IndependentPairs = independent,
                PairIds = entries.Select(pair => pair.PairId).ToList(),
                BlockingReasons = reasons.Distinct().ToList(),
                Bands = bands
            });
        }

        return summaries;
    }

    private List<RepeatabilityGroup> Repeatability(AcousticProject project, Dictionary<Guid, MeasurementCondition> conditions)
    {
        var groups = project.Trials.Where(trial => !string.IsNullOrWhiteSpace(trial.RepeatGroup))
            .GroupBy(trial => (trial.RepeatGroup, trial.Kind, Locations: Key(trial.LocationIds), trial.Source));
        var output = new List<RepeatabilityGroup>();
        foreach (var group in groups)
        {
            var entries = group.ToList();
            var reasons = comparability.MissingFields(conditions[entries[0].ConditionId], project.Settings);
            for (var index = 0; index < entries.Count; index++)
            {
                for (var other = index + 1; other < entries.Count; other++)
                {
                    reasons.AddRange(comparability.Inspect(conditions[entries[index].ConditionId],
                        conditions[entries[other].ConditionId], project.Settings));
                }
            }

            var bands = new List<RepeatabilityBand>();
            foreach (var frequency in Frequencies(entries))
            {
                var levels = entries.Select(trial => trial.LevelAt(frequency)).Where(value => value.HasValue)
                    .Select(value => value!.Value).ToList();
                var statistics = reasons.Count > 0 ? new DescriptiveStatistics { Count = levels.Count } : AcousticMath.Describe(levels);
                bands.Add(new RepeatabilityBand
                {
                    Frequency = frequency,
                    MissingCount = entries.Count - levels.Count,
                    Statistics = statistics,
                    Status = reasons.Count > 0 ? ResultStatus.Blocked : levels.Count < 2 ? ResultStatus.Insufficient :
                        statistics.StandardDeviation > project.Settings.RepeatabilityThreshold ? ResultStatus.Alert : ResultStatus.Within
                });
            }

            output.Add(new RepeatabilityGroup
            {
                Name = group.Key.RepeatGroup,
                Kind = group.Key.Kind,
                Source = group.Key.Source,
                LocationIds = [.. entries[0].LocationIds],
                TrialIds = entries.Select(trial => trial.Id).ToList(),
                BlockingReasons = reasons.Distinct().ToList(),
                Threshold = project.Settings.RepeatabilityThreshold,
                Bands = bands
            });
        }

        return output;
    }

    private CoverageReport BuildCoverage(AcousticProject project, List<PairResult> pairs)
    {
        return new CoverageReport
        {
            TrialTypes = Enum.GetValues<TrialKind>().ToDictionary(kind => kind, kind => project.Trials.Count(trial => trial.Kind == kind)),
            Conditions = project.Conditions.Select(condition => new ConditionCoverage(condition.Id, condition.Name,
                project.Trials.Count(trial => trial.ConditionId == condition.Id), comparability.MissingFields(condition, project.Settings))).ToList(),
            Locations = project.Locations.Select(location => new LocationCoverage(
                location.Id, location.Name,
                project.Trials.Count(trial => trial.Kind == TrialKind.Single && trial.LocationIds.Contains(location.Id)),
                project.Trials.Count(trial => trial.Kind == TrialKind.Combination && trial.LocationIds.Contains(location.Id)),
                pairs.Count(pair => pair.Purpose == PairPurpose.Effect && pair.Status == ResultStatus.Valid && pair.LocationIds.Contains(location.Id)))).ToList(),
            Frequencies = Frequencies(project.Trials).Select(frequency =>
            {
                var declared = project.Trials.Where(trial => trial.Bands.Any(band => band.Frequency == frequency)).ToList();
                var valid = declared.Count(trial => trial.LevelAt(frequency).HasValue);
                return new FrequencyCoverage(frequency, valid, declared.Count - valid, project.Trials.Count - declared.Count);
            }).ToList(),
            Timeline = project.Trials.OrderBy(trial => trial.Timestamp).Select(trial => new TimelineEntry(
                trial.Id, trial.Name, trial.Kind, trial.Source, trial.Timestamp, trial.ValidBandCount,
                pairs.Count(pair => pair.TargetId == trial.Id))).ToList()
        };
    }

    public static List<double> Frequencies(IEnumerable<AcousticTrial> trials)
    {
        return trials.SelectMany(trial => trial.Bands.Select(band => band.Frequency)).Distinct().Order().ToList();
    }

    public static string Key(IEnumerable<Guid> ids)
    {
        return string.Join("+", ids.Order());
    }

    public static double? Total(AcousticTrial trial, MeasurementCondition condition)
    {
        if (!condition.NonOverlappingBands || trial.Bands.Count == 0 || trial.Bands.Any(band => !band.Level.HasValue))
        {
            return null;
        }

        return AcousticMath.EnergySum(trial.Bands.Select(band => band.Level!.Value));
    }
}
