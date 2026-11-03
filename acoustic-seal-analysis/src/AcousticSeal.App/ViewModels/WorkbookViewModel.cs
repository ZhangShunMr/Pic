using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.App.ViewModels;

public sealed class WorkbookViewModel : ObservableObject
{
    public HashSet<Guid> Selected { get; } = [];
    public string Minimum { get; set; } = "";
    public string Maximum { get; set; } = "";
    public string Search { get; set; } = "";
    public InputWorkbookResult? Result { get; private set; }
    public string DownloadUrl { get; private set; } = "";
    public string InputHash { get; private set; } = "";
    public long Revision { get; private set; }

    public void Toggle(Guid id, bool selected)
    {
        if (selected)
        {
            Selected.Add(id);
        }
        else
        {
            Selected.Remove(id);
        }

        Result = null;
        DownloadUrl = "";
        Notify();
    }

    public void Build(AcousticProject project)
    {
        var minimum = InputNumber.Optional(Minimum, "核对频带下限", 1, 100000);
        var maximum = InputNumber.Optional(Maximum, "核对频带上限", 1, 100000);
        Result = new InputWorkbook().Build(project, Selected.ToList(), minimum, maximum);
        InputHash = ProjectCodec.Fingerprint(project);
        Revision = project.Revision;
        DownloadUrl = "";
        Notify();
    }

    public List<AcousticTrial> Candidates(AcousticProject project)
    {
        return project.Trials.Where(trial => string.IsNullOrWhiteSpace(Search) ||
            (trial.Name + trial.RepeatGroup).Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)).Take(100).ToList();
    }

    public bool IsStale(AcousticProject project)
    {
        return Result is not null && InputHash != ProjectCodec.Fingerprint(project);
    }

    public void Export(AcousticProject project, DownloadVault vault)
    {
        if (Result is null || IsStale(project))
        {
            throw new DomainValidationException("workbook", "工作表为空或项目已改变，请重新生成核对表");
        }

        DownloadUrl = vault.Add(new ExportDocument("原始输入核对表.csv", "text/csv; charset=utf-8", new InputWorkbook().Csv(Result)));
        Notify();
    }

    public void Clear()
    {
        Selected.Clear();
        Minimum = "";
        Maximum = "";
        Search = "";
        Result = null;
        InputHash = "";
        DownloadUrl = "";
        Notify();
    }
}
