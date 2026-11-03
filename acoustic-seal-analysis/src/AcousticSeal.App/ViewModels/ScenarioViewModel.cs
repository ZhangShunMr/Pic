using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.App.ViewModels;

public sealed class ScenarioViewModel : ObservableObject
{
    public string Parameter { get; set; } = nameof(AnalysisSettings.DriftThreshold);
    public string Values { get; set; } = "0.2,0.5,1";
    public ScenarioComparison? Result { get; private set; }
    public string DownloadUrl { get; private set; } = "";
    public string AppliedRequest { get; private set; } = "";
    public bool DraftChanged => AppliedRequest != Parameter + ":" + Values;

    public void Calculate(AcousticProject project)
    {
        var setting = project.Settings.Describe().First(item => item.Key == Parameter);
        var parts = Values.Split(',', StringSplitOptions.TrimEntries);
        var values = parts.Select((text, index) => InputNumber.Required(text,
            "情景参数第" + (index + 1) + "项", setting.Minimum, setting.Maximum)).ToList();
        Result = new SettingsScenarios().Compare(project, Parameter, values);
        AppliedRequest = Parameter + ":" + Values;
        DownloadUrl = "";
        Notify();
    }

    public bool IsStale(AcousticProject project)
    {
        return Result is not null && Result.InputHash != ProjectCodec.Fingerprint(project);
    }

    public void Export(AcousticProject project, DownloadVault vault)
    {
        if (Result is null || IsStale(project) || DraftChanged)
        {
            throw new DomainValidationException("scenario", "情景结果为空或输入已改变，请重新计算后下载");
        }

        DownloadUrl = vault.Add(new SettingsScenarios().Export(Result));
        Notify();
    }

    public void Clear()
    {
        Result = null;
        DownloadUrl = "";
        AppliedRequest = "";
        Notify();
    }
}
