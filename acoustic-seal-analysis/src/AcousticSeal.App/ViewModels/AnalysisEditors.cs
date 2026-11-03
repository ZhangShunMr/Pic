using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.Core.Operations;

namespace AcousticSeal.App.ViewModels;

public sealed class PairEditor : EditorViewModel
{
    public Guid BaselineId { get; set; }
    public Guid TargetId { get; set; }
    public PairPurpose Purpose { get; set; }
    public string Notes { get; set; } = "";

    public PairEditor(TrialPair? item = null)
    {
        if (item is null)
        {
            return;
        }

        Id = item.Id;
        IsExisting = true;
        BaselineId = item.BaselineId;
        TargetId = item.TargetId;
        Purpose = item.Purpose;
        Notes = item.Notes;
    }

    public TrialPair ToModel()
    {
        return new TrialPair
        {
            Id = Id,
            BaselineId = BaselineId,
            TargetId = TargetId,
            Purpose = Purpose,
            Notes = Text(Notes)
        };
    }
}

public sealed class SettingInput
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "";
    public double Default { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public string Value { get; set; } = "";
}

public sealed class SettingsEditor : EditorViewModel
{
    public List<SettingInput> Inputs { get; } = [];
    public bool RequireSampleRateMatch { get; set; }
    public bool RequireSynchronizationMatch { get; set; }

    public SettingsEditor(AnalysisSettings settings)
    {
        IsExisting = true;
        RequireSampleRateMatch = settings.RequireSampleRateMatch;
        RequireSynchronizationMatch = settings.RequireSynchronizationMatch;
        Inputs.AddRange(settings.Describe().Select(setting => new SettingInput
        {
            Key = setting.Key,
            Label = setting.Label,
            Unit = setting.Unit,
            Default = setting.Default,
            Minimum = setting.Minimum,
            Maximum = setting.Maximum,
            Value = InputNumber.Format(setting.Value)
        }));
    }

    public AnalysisSettings ToModel()
    {
        var values = Inputs.ToDictionary(input => input.Key,
            input => InputNumber.Required(input.Value, input.Label, input.Minimum, input.Maximum));
        return new AnalysisSettings
        {
            SpeedTolerance = values[nameof(AnalysisSettings.SpeedTolerance)],
            RpmTolerance = values[nameof(AnalysisSettings.RpmTolerance)],
            TemperatureTolerance = values[nameof(AnalysisSettings.TemperatureTolerance)],
            DurationTolerance = values[nameof(AnalysisSettings.DurationTolerance)],
            DriftThreshold = values[nameof(AnalysisSettings.DriftThreshold)],
            RecoveryThreshold = values[nameof(AnalysisSettings.RecoveryThreshold)],
            RepeatabilityThreshold = values[nameof(AnalysisSettings.RepeatabilityThreshold)],
            MinimumEffectDisplay = values[nameof(AnalysisSettings.MinimumEffectDisplay)],
            RequireSampleRateMatch = RequireSampleRateMatch,
            RequireSynchronizationMatch = RequireSynchronizationMatch
        };
    }

    public void ResetDefaults()
    {
        foreach (var input in Inputs)
        {
            input.Value = InputNumber.Format(input.Default);
        }

        RequireSampleRateMatch = false;
        RequireSynchronizationMatch = false;
        MarkDirty();
    }
}

public sealed class ImportEditor : EditorViewModel
{
    public string Content { get; set; } = "";
    public string Filename { get; set; } = "";
    public ImportMode Mode { get; set; }
    public string Delimiter { get; set; } = ",";
    public ImportPreview? Preview { get; private set; }

    public void Invalidate()
    {
        Preview = null;
        MarkDirty();
    }

    public void SetPreview(ImportPreview value)
    {
        Preview = value;
        MarkClean();
    }

    public void ClearPreview()
    {
        Preview = null;
        Notify(nameof(Preview));
    }

    public char DelimiterCharacter()
    {
        return Delimiter switch
        {
            ";" => ';',
            "tab" => '\t',
            _ => ','
        };
    }
}

public sealed class BatchPairEditor : EditorViewModel
{
    public Guid BaselineId { get; set; }
    public PairPurpose Purpose { get; set; }
    public HashSet<Guid> TargetIds { get; } = [];
    public PairingPreview? Preview { get; private set; }

    public void Toggle(Guid id, bool selected)
    {
        if (selected)
        {
            TargetIds.Add(id);
        }
        else
        {
            TargetIds.Remove(id);
        }

        Invalidate();
    }

    public void Invalidate()
    {
        Preview = null;
        MarkDirty();
    }

    public void SetPreview(PairingPreview value)
    {
        Preview = value;
        MarkClean();
    }
}

public sealed class AnalysisFilterViewModel : ObservableObject
{
    public string MinimumFrequency { get; set; } = "";
    public string MaximumFrequency { get; set; } = "";
    public PairPurpose? Purpose { get; set; }
    public SourceKind? Source { get; set; }
    public ResultStatus? Status { get; set; }
    public Guid? Location { get; set; }
    public string Search { get; set; } = "";
    public double? AppliedMinimum { get; private set; }
    public double? AppliedMaximum { get; private set; }
    public string AppliedDescription => "频带范围：" + (AppliedMinimum?.ToString() ?? "不限") + "—" + (AppliedMaximum?.ToString() ?? "不限") + " Hz";

    public void ApplyRange()
    {
        var low = InputNumber.Optional(MinimumFrequency, "扫描频带下限", 1, 100000);
        var high = InputNumber.Optional(MaximumFrequency, "扫描频带上限", 1, 100000);
        if (low > high)
        {
            throw new DomainValidationException("scan", "扫描下限不得大于上限");
        }

        AppliedMinimum = low;
        AppliedMaximum = high;
        Notify();
    }

    public void Reset()
    {
        MinimumFrequency = "";
        MaximumFrequency = "";
        AppliedMinimum = null;
        AppliedMaximum = null;
        Purpose = null;
        Source = null;
        Status = null;
        Location = null;
        Search = "";
        Notify();
    }

    public bool IncludesFrequency(double frequency)
    {
        return (!AppliedMinimum.HasValue || frequency >= AppliedMinimum) &&
            (!AppliedMaximum.HasValue || frequency <= AppliedMaximum);
    }

    public List<PairResult> Pairs(AnalysisReport report)
    {
        return report.Pairs.Where(pair =>
            (!Purpose.HasValue || pair.Purpose == Purpose) &&
            (!Source.HasValue || pair.Source == Source) &&
            (!Status.HasValue || pair.Status == Status) &&
            (!Location.HasValue || pair.LocationIds.Contains(Location.Value)) &&
            (string.IsNullOrWhiteSpace(Search) || pair.Title.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public List<BandDifference> Bands(PairResult pair)
    {
        return pair.Bands.Where(band => IncludesFrequency(band.Frequency)).ToList();
    }

    public List<double> MatrixFrequencies(IEnumerable<PairResult> pairs)
    {
        return pairs.SelectMany(pair => pair.Bands.Select(band => band.Frequency))
            .Distinct().Where(IncludesFrequency).Order().ToList();
    }

    public PairResult ChartPair(PairResult pair)
    {
        return new PairResult
        {
            PairId = pair.PairId,
            BaselineName = pair.BaselineName,
            TargetName = pair.TargetName,
            Source = pair.Source,
            Status = pair.Status,
            Bands = Bands(pair)
        };
    }
}
