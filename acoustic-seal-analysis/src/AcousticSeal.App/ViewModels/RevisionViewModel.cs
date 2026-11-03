using System.Text.Json;
using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.App.ViewModels;

public sealed class RevisionViewModel : ObservableObject
{
    private AcousticProject? left;
    private AcousticProject? right;
    public string LeftFilename { get; private set; } = "";
    public string RightFilename { get; private set; } = "";
    public string LeftIdentity => Describe(left);
    public string RightIdentity => Describe(right);
    public List<string> MigrationNotes { get; private set; } = [];
    public RevisionComparisonResult? Result { get; private set; }
    public string Search { get; set; } = "";
    public ChangeKind? Kind { get; set; }
    public int Page { get; private set; }
    public string DownloadUrl { get; private set; } = "";
    public string DownloadFilename { get; private set; } = "";
    public bool CanCompare => left is not null && right is not null;
    public int PageSize => 50;

    public void Load(bool isLeft, byte[] bytes, string filename)
    {
        var decoded = new ProjectCodec().Decode(bytes);
        if (isLeft)
        {
            left = decoded.Project;
            LeftFilename = filename;
        }
        else
        {
            right = decoded.Project;
            RightFilename = filename;
        }

        MigrationNotes = decoded.Notes;
        Result = null;
        DownloadUrl = "";
        Notify();
    }

    public void UseCurrent(bool isLeft, AcousticProject project)
    {
        if (isLeft)
        {
            left = ProjectCodec.Clone(project);
            LeftFilename = "当前已应用工作副本（快照）";
        }
        else
        {
            right = ProjectCodec.Clone(project);
            RightFilename = "当前已应用工作副本（快照）";
        }

        Result = null;
        DownloadUrl = "";
        Notify();
    }

    public void Compare()
    {
        if (left is null || right is null)
        {
            throw new DomainValidationException("compare", "先载入左右两份项目或使用当前快照");
        }

        Result = new RevisionComparison().Compare(left, right);
        Page = 0;
        Notify();
    }

    public List<RevisionChange> Filtered()
    {
        return Result?.Changes.Where(change => (!Kind.HasValue || change.Kind == Kind) &&
            (string.IsNullOrWhiteSpace(Search) || (change.Path + change.Before + change.After)
                .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToList() ?? [];
    }

    public List<RevisionChange> Visible()
    {
        return Filtered().Skip(Page * PageSize).Take(PageSize).ToList();
    }

    public void Filter()
    {
        Page = 0;
        Notify();
    }

    public void MovePage(int change)
    {
        var maximum = Math.Max(0, (Filtered().Count - 1) / PageSize);
        Page = Math.Clamp(Page + change, 0, maximum);
        Notify();
    }

    public void Export(DownloadVault vault)
    {
        if (Result is null)
        {
            throw new DomainValidationException("compare", "先执行内容核对");
        }

        DownloadFilename = "项目版本核对.analysis.json";
        DownloadUrl = vault.Add(new ExportDocument(DownloadFilename, "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(Result, ProjectCodec.Options)));
        Notify();
    }

    public void Clear()
    {
        left = null;
        right = null;
        LeftFilename = "";
        RightFilename = "";
        Result = null;
        MigrationNotes = [];
        DownloadUrl = "";
        Search = "";
        Kind = null;
        Page = 0;
        Notify();
    }

    public static string KindText(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added => "新增",
            ChangeKind.Removed => "移除",
            _ => "修改"
        };
    }

    private static string Describe(AcousticProject? project)
    {
        return project is null ? "尚未载入" : project.Name + " · 修订" + project.Revision + " · " + project.Id;
    }
}
