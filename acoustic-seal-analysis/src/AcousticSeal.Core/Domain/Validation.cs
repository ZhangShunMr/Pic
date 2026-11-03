using System.Globalization;

namespace AcousticSeal.Core.Domain;

public sealed record ValidationMessage(string Path, string Message)
{
    public override string ToString()
    {
        return Path + "：" + Message;
    }
}

public sealed class DomainValidationException : Exception
{
    public IReadOnlyList<ValidationMessage> Problems { get; }

    public DomainValidationException(string path, string message)
        : this([new ValidationMessage(path, message)])
    {
    }

    public DomainValidationException(IReadOnlyList<ValidationMessage> problems)
        : base(string.Join("；", problems.Select(problem => problem.ToString())))
    {
        Problems = problems;
    }
}

public sealed class ProjectValidator
{
    public IReadOnlyList<ValidationMessage> Inspect(AcousticProject project)
    {
        var errors = new List<ValidationMessage>();
        if (project.SchemaVersion != Software.SchemaVersion)
        {
            errors.Add(new("schemaVersion", "格式版本不受支持，请保留原文件"));
        }

        CheckId(project.Id, "project.id", errors);
        CheckText(project.Name, "project.name", 160, true, errors);
        CheckText(project.Owner, "project.owner", 120, false, errors);
        CheckText(project.VehicleProgramme, "project.vehicleProgramme", 200, false, errors);
        CheckText(project.Objective, "project.objective", 4000, false, errors);
        CheckText(project.Description, "project.description", 8000, false, errors);
        CheckText(project.SoftwareVersion, "softwareVersion", 40, true, errors);
        CheckEnum(project.Status, "project.status", errors);
        if (project.Revision < 0)
        {
            errors.Add(new("revision", "修订号不得为负"));
        }

        CheckDate(project.CreatedAt, "createdAt", errors);
        CheckDate(project.UpdatedAt, "updatedAt", errors);
        if (project.Locations is null || project.Conditions is null || project.Trials is null ||
            project.Pairs is null || project.Settings is null || project.Audit is null || project.Reviews is null)
        {
            errors.Add(new("project", "对象集合、设置、审计或复核不得为null"));
            return errors;
        }

        CheckCount(project.Locations.Count, 200, "locations", errors);
        CheckCount(project.Conditions.Count, 500, "conditions", errors);
        CheckCount(project.Trials.Count, Software.MaximumTrials, "trials", errors);
        CheckCount(project.Pairs.Count, Software.MaximumPairs, "pairs", errors);
        CheckCount(project.Audit.Count, 10000, "audit", errors);
        CheckCount(project.Reviews.Count, 2000, "reviews", errors);
        if (project.Locations.Any(item => item is null) || project.Conditions.Any(item => item is null) ||
            project.Trials.Any(item => item is null) || project.Pairs.Any(item => item is null) ||
            project.Audit.Any(item => item is null) || project.Reviews.Any(item => item is null))
        {
            errors.Add(new("project", "集合中不可包含null对象"));
            return errors;
        }

        CheckUnique(project.Locations.Select(item => item.Id), "locations", errors);
        CheckUnique(project.Conditions.Select(item => item.Id), "conditions", errors);
        CheckUnique(project.Trials.Select(item => item.Id), "trials", errors);
        CheckUnique(project.Pairs.Select(item => item.Id), "pairs", errors);
        CheckUnique(project.Audit.Select(item => item.Id), "audit", errors);
        CheckUnique(project.Reviews.Select(item => item.Id), "reviews", errors);
        foreach (var location in project.Locations)
        {
            ValidateLocation(location, errors);
        }

        foreach (var condition in project.Conditions)
        {
            ValidateCondition(condition, errors);
        }

        var locationIds = project.Locations.Select(item => item.Id).ToHashSet();
        var conditionIds = project.Conditions.Select(item => item.Id).ToHashSet();
        foreach (var trial in project.Trials)
        {
            ValidateTrial(trial, locationIds, conditionIds, errors);
        }

        var trialMap = project.Trials.GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var pair in project.Pairs)
        {
            ValidatePair(pair, trialMap, errors);
        }

        var duplicatePair = project.Pairs.GroupBy(pair => (pair.BaselineId, pair.TargetId, pair.Purpose))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePair is not null)
        {
            errors.Add(new("pairs", "同一用途的相同基线和目标不可重复"));
        }

        foreach (var setting in project.Settings.Describe())
        {
            CheckNumber(setting.Value, setting.Minimum, setting.Maximum, "settings." + setting.Key, false, errors);
        }

        foreach (var entry in project.Audit)
        {
            CheckId(entry.Id, "audit.id", errors);
            CheckDate(entry.Time, "audit.time", errors);
            CheckText(entry.Action, "audit.action", 120, true, errors);
            CheckText(entry.Entity, "audit.entity", 200, false, errors);
            CheckText(entry.Detail, "audit.detail", 16000, false, errors);
            CheckText(entry.InputHash, "audit.inputHash", 64, false, errors);
            if (entry.Revision < 0 || entry.Revision > project.Revision)
            {
                errors.Add(new("audit.revision", "审计修订号超出项目范围"));
            }
        }

        foreach (var review in project.Reviews)
        {
            CheckId(review.Id, "reviews.id", errors);
            CheckDate(review.Time, "reviews.time", errors);
            CheckText(review.Reviewer, "reviews.reviewer", 120, true, errors);
            CheckText(review.Comment, "reviews.comment", 4000, false, errors);
            CheckText(review.SettingsJson, "reviews.settingsJson", 8000, true, errors);
            CheckText(review.InputHash, "reviews.inputHash", 64, true, errors);
            if (review.BlockingReasons is null)
            {
                errors.Add(new("reviews.blockingReasons", "复核阻断原因列表不得为null"));
            }
            else
            {
                CheckCount(review.BlockingReasons.Count, 5000, "reviews.blockingReasons", errors);
                foreach (var reason in review.BlockingReasons)
                {
                    CheckText(reason, "reviews.blockingReasons", 2000, true, errors);
                }
            }
        }

        return errors;
    }

    public void Ensure(AcousticProject project)
    {
        var errors = Inspect(project);
        if (errors.Count != 0)
        {
            throw new DomainValidationException(errors);
        }
    }

    private static void ValidateLocation(SealLocation location, List<ValidationMessage> errors)
    {
        var path = "locations[" + location.Id + "]";
        CheckText(location.Name, path + ".name", 120, true, errors);
        CheckText(location.Zone, path + ".zone", 120, false, errors);
        CheckText(location.Material, path + ".material", 1000, false, errors);
        CheckText(location.Extent, path + ".extent", 1000, false, errors);
        CheckText(location.Procedure, path + ".procedure", 4000, false, errors);
        CheckText(location.RecoveryProcedure, path + ".recoveryProcedure", 4000, false, errors);
        CheckText(location.Reference, path + ".reference", 1000, false, errors);
        CheckText(location.Notes, path + ".notes", 4000, false, errors);
    }

    private static void ValidateCondition(MeasurementCondition condition, List<ValidationMessage> errors)
    {
        var path = "conditions[" + condition.Id + "]";
        CheckText(condition.Name, path + ".name", 120, true, errors);
        foreach (var field in condition.TextFields())
        {
            CheckText(field.Value, path + "." + field.Key, 500, false, errors);
        }

        CheckNumber(condition.Speed, 0, 500, path + ".speed", true, errors);
        CheckNumber(condition.Rpm, 0, 30000, path + ".rpm", true, errors);
        CheckNumber(condition.Temperature, -80, 100, path + ".temperature", true, errors);
        CheckNumber(condition.Duration, 0.001, 86400, path + ".duration", true, errors);
        CheckNumber(condition.SampleRate, 1, 1000000, path + ".sampleRate", true, errors);
        CheckText(condition.Synchronization, path + ".synchronization", 500, false, errors);
        CheckText(condition.Notes, path + ".notes", 4000, false, errors);
        if (condition.Weighting is not ("" or "A" or "C" or "Z"))
        {
            errors.Add(new(path + ".weighting", "频率计权仅支持A、C、Z或草稿空白"));
        }

        if (condition.BandType is not ("" or "octave" or "third-octave" or "custom"))
        {
            errors.Add(new(path + ".bandType", "频带制须为octave、third-octave或custom"));
        }

        if (condition.Unit is not ("" or Software.LevelUnit))
        {
            errors.Add(new(path + ".unit", "仅支持" + Software.LevelUnit));
        }
    }

    private static void ValidateTrial(
        AcousticTrial trial,
        HashSet<Guid> locations,
        HashSet<Guid> conditions,
        List<ValidationMessage> errors)
    {
        var path = "trials[" + trial.Id + "]";
        CheckText(trial.Name, path + ".name", 160, true, errors);
        CheckEnum(trial.Kind, path + ".kind", errors);
        CheckEnum(trial.Source, path + ".source", errors);
        CheckDate(trial.Timestamp, path + ".timestamp", errors);
        CheckText(trial.RepeatGroup, path + ".repeatGroup", 120, false, errors);
        CheckText(trial.SourceNote, path + ".sourceNote", 4000, true, errors);
        CheckText(trial.OriginalFile, path + ".originalFile", 1000, false, errors);
        CheckText(trial.Operator, path + ".operator", 120, false, errors);
        CheckText(trial.Preparation, path + ".preparation", 4000, false, errors);
        CheckText(trial.Notes, path + ".notes", 4000, false, errors);
        if (!conditions.Contains(trial.ConditionId))
        {
            errors.Add(new(path + ".conditionId", "引用的工况不存在"));
        }

        if (trial.LocationIds is null || trial.Bands is null)
        {
            errors.Add(new(path, "封堵位置集合与频带集合不得为null"));
            return;
        }

        if (trial.LocationIds.Distinct().Count() != trial.LocationIds.Count)
        {
            errors.Add(new(path + ".locationIds", "封堵位置不可重复"));
        }

        if (trial.LocationIds.Any(id => !locations.Contains(id)))
        {
            errors.Add(new(path + ".locationIds", "引用的封堵位置不存在"));
        }

        var count = trial.LocationIds.Count;
        if (trial.Kind is TrialKind.Baseline or TrialKind.Recovery && count != 0)
        {
            errors.Add(new(path + ".locationIds", "基线和恢复不得选择封堵位置"));
        }

        if (trial.Kind == TrialKind.Single && count != 1)
        {
            errors.Add(new(path + ".locationIds", "单项封堵须选择一个位置"));
        }

        if (trial.Kind == TrialKind.Combination && count < 2)
        {
            errors.Add(new(path + ".locationIds", "组合封堵至少选择两个位置"));
        }

        CheckCount(trial.Bands.Count, Software.MaximumBands, path + ".bands", errors);
        if (trial.Bands.Any(band => band is null))
        {
            errors.Add(new(path + ".bands", "频带不可为null对象"));
            return;
        }

        foreach (var band in trial.Bands)
        {
            var bandPath = path + ".bands[" + band.Frequency.ToString(CultureInfo.InvariantCulture) + "]";
            CheckNumber(band.Frequency, 1, 100000, bandPath + ".frequency", false, errors);
            CheckNumber(band.Level, -100, 200, bandPath + ".level", true, errors);
            CheckText(band.Note, bandPath + ".note", 1000, false, errors);
        }

        if (trial.Bands.GroupBy(band => band.Frequency).Any(group => group.Count() > 1))
        {
            errors.Add(new(path + ".bands", "同一试验频率不可重复"));
        }
    }

    private static void ValidatePair(TrialPair pair, Dictionary<Guid, AcousticTrial> trials, List<ValidationMessage> errors)
    {
        var path = "pairs[" + pair.Id + "]";
        CheckText(pair.Notes, path + ".notes", 4000, false, errors);
        CheckEnum(pair.Purpose, path + ".purpose", errors);
        if (!trials.TryGetValue(pair.BaselineId, out var baseline) ||
            !trials.TryGetValue(pair.TargetId, out var target))
        {
            errors.Add(new(path, "配对引用的试验不存在"));
            return;
        }

        if (baseline.Id == target.Id)
        {
            errors.Add(new(path, "不能与自身配对"));
        }

        if (baseline.Kind != TrialKind.Baseline)
        {
            errors.Add(new(path + ".baselineId", "基准须为基线试验"));
        }

        var allowed = pair.Purpose switch
        {
            PairPurpose.Effect => target.Kind is TrialKind.Single or TrialKind.Combination,
            PairPurpose.Drift => target.Kind == TrialKind.Baseline,
            PairPurpose.Recovery => target.Kind == TrialKind.Recovery,
            _ => false
        };
        if (!allowed)
        {
            errors.Add(new(path + ".targetId", "目标试验类型与配对用途不匹配"));
        }
    }

    private static void CheckText(string? value, string path, int maximum, bool required, List<ValidationMessage> errors)
    {
        if (value is null)
        {
            errors.Add(new(path, "文本不得为null"));
            return;
        }

        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(path, "不能为空"));
        }

        if (value.Length > maximum)
        {
            errors.Add(new(path, $"最多{maximum}字符"));
        }

        if (value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            errors.Add(new(path, "包含不可用控制字符"));
        }
    }

    private static void CheckNumber(double? value, double minimum, double maximum, string path, bool nullable, List<ValidationMessage> errors)
    {
        if (!value.HasValue)
        {
            if (!nullable)
            {
                errors.Add(new(path, "数值不能为空"));
            }

            return;
        }

        if (!double.IsFinite(value.Value) || value < minimum || value > maximum)
        {
            errors.Add(new(path, $"须为{minimum}至{maximum}的有限数值"));
        }
    }

    private static void CheckId(Guid id, string path, List<ValidationMessage> errors)
    {
        if (id == Guid.Empty)
        {
            errors.Add(new(path, "对象标识不可为空"));
        }
    }

    private static void CheckUnique(IEnumerable<Guid> ids, string path, List<ValidationMessage> errors)
    {
        var values = ids.ToList();
        if (values.Contains(Guid.Empty) || values.Distinct().Count() != values.Count)
        {
            errors.Add(new(path, "对象标识为空或重复"));
        }
    }

    private static void CheckDate(DateTimeOffset date, string path, List<ValidationMessage> errors)
    {
        if (date.Year < 1900 || date.Year > 2200)
        {
            errors.Add(new(path, "日期范围须为1900至2200年，保留时区"));
        }
    }

    private static void CheckCount(int count, int maximum, string path, List<ValidationMessage> errors)
    {
        if (count > maximum)
        {
            errors.Add(new(path, $"最多允许{maximum}项"));
        }
    }

    private static void CheckEnum<T>(T value, string path, List<ValidationMessage> errors) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add(new(path, "枚举值无效"));
        }
    }
}
