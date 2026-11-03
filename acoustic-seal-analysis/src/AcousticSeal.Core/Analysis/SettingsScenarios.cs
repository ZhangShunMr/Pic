using System.Text.Json;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.Core.Analysis;

public sealed record ScenarioPairState(Guid PairId, string Title, ResultStatus Status, ResultStatus? Screening, List<string> Reasons);
public sealed record SettingsScenario(double Value, int ValidPairs, int BlockedPairs, int ScreeningAlerts, int RepeatabilityAlerts, List<ScenarioPairState> Pairs);
public sealed record ScenarioComparison(string InputHash, string Parameter, string Label, string Unit, double Original, List<SettingsScenario> Scenarios, string Note);

public sealed class SettingsScenarios
{
    public ScenarioComparison Compare(AcousticProject project, string key, IReadOnlyList<double> values)
    {
        new ProjectValidator().Ensure(project);
        var setting = project.Settings.Describe().FirstOrDefault(setting => setting.Key == key)
            ?? throw new DomainValidationException("scenario.parameter", "未知可扫描参数");
        if (values.Count == 0 || values.Count > 20 || values.Distinct().Count() != values.Count)
        {
            throw new DomainValidationException("scenario.values", "请提供1至20个互不重复的参数值");
        }

        foreach (var value in values)
        {
            if (!double.IsFinite(value) || value < setting.Minimum || value > setting.Maximum)
            {
                throw new DomainValidationException("scenario.values", $"{setting.Label}允许{setting.Minimum}至{setting.Maximum}{setting.Unit}");
            }
        }

        var scenarios = new List<SettingsScenario>();
        foreach (var value in values)
        {
            var copy = ProjectCodec.Clone(project);
            Set(copy.Settings, key, value);
            var report = new AnalysisEngine().Calculate(copy);
            scenarios.Add(new SettingsScenario(
                value,
                report.ValidPairCount,
                report.BlockedPairCount,
                report.Pairs.Count(pair => pair.Screening?.Status == ResultStatus.Alert),
                report.Repeatability.Sum(group => group.Bands.Count(band => band.Status == ResultStatus.Alert)),
                report.Pairs.Select(pair => new ScenarioPairState(pair.PairId, pair.Title, pair.Status,
                    pair.Screening?.Status, pair.BlockingReasons)).ToList()));
        }

        return new ScenarioComparison(ProjectCodec.Fingerprint(project), key, setting.Label, setting.Unit, setting.Value, scenarios,
            "仅对相同输入按假设工程参数重新判读，不改测量、不推荐放宽规则、不代表标准认证或组合预测。当前项目参数保持不变。");
    }

    public static void Set(AnalysisSettings settings, string key, double value)
    {
        switch (key)
        {
            case nameof(AnalysisSettings.SpeedTolerance):
                settings.SpeedTolerance = value;
                break;
            case nameof(AnalysisSettings.RpmTolerance):
                settings.RpmTolerance = value;
                break;
            case nameof(AnalysisSettings.TemperatureTolerance):
                settings.TemperatureTolerance = value;
                break;
            case nameof(AnalysisSettings.DurationTolerance):
                settings.DurationTolerance = value;
                break;
            case nameof(AnalysisSettings.DriftThreshold):
                settings.DriftThreshold = value;
                break;
            case nameof(AnalysisSettings.RecoveryThreshold):
                settings.RecoveryThreshold = value;
                break;
            case nameof(AnalysisSettings.RepeatabilityThreshold):
                settings.RepeatabilityThreshold = value;
                break;
            case nameof(AnalysisSettings.MinimumEffectDisplay):
                settings.MinimumEffectDisplay = value;
                break;
            default:
                throw new DomainValidationException("scenario.parameter", "未知参数");
        }
    }

    public ExportDocument Export(ScenarioComparison comparison)
    {
        return new ExportDocument("工程规则情景.analysis.json", "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(comparison, ProjectCodec.Options));
    }
}
