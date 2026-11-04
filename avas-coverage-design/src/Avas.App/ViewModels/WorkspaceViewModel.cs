using System.ComponentModel;
using Avas.Core.Analysis;
using Avas.Core.Audio;
using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.App.ViewModels;

public sealed partial class WorkspaceViewModel : INotifyPropertyChanged
{
    private readonly ProjectStore store;
    private readonly DownloadVault vault;
    private readonly Stack<Project> undo = new();
    private readonly Stack<Project> redo = new();
    private string? diskHash;
    private string savedFingerprint = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public Project Project { get; private set; } = new();
    public Project Draft { get; private set; } = new();
    public Calculation? Result { get; private set; }
    public string Tab { get; private set; } = "项目";
    public string Status { get; private set; } = "从空白新建项目，或通过独立入口载入模拟示例。";
    public List<string> Errors { get; private set; } = [];
    public bool HasDraft { get; private set; }
    public bool Busy { get; private set; }
    public bool Dirty => ProjectCodec.Json(Project) != savedFingerprint;
    public bool Stale => Result is null || Result.InputHash != ProjectCodec.InputHash(Project);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public Guid? SelectedSpeakerId { get; private set; }
    public Guid? SelectedListenerId { get; private set; }
    public Speaker? SpeakerDraft => Draft.Speakers.SingleOrDefault(x => x.Id == SelectedSpeakerId);
    public Listener? ListenerDraft => Draft.Listeners.SingleOrDefault(x => x.Id == SelectedListenerId);
    public PointResult? SelectedPoint => Result?.Listeners.SingleOrDefault(x => x.ListenerId == SelectedListenerId);
    public bool CoverageOnly { get; set; }
    public string MapSvg => ChartRenderer.Map(Project, Stale ? null : Result, CoverageOnly);
    public List<StoredProject> Library { get; private set; } = [];
    public string DataDirectory => store.DirectoryPath;
    public string? LastDownload { get; private set; }
    public string? LastFileName { get; private set; }
    public string SnapshotName { get; set; } = "方案A";
    public string BaselineId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public Comparison? Comparison { get; private set; }
    public ScanRequest ScanRequest { get; set; } = new();
    public ScanResult? Scan { get; private set; }
    public string CsvText { get; set; } = SpectrumCsv.Template();
    public bool ImportBackground { get; set; }
    public SpectrumPreview? Preview { get; private set; }
    private string? previewTarget;
    private string? previewText;
    public double AudioReference { get; set; } = 70;
    public double AuditionSeconds { get; set; } = 3;
    public double PlaybackReference { get; set; } = 100;
    public bool IncludeBackground { get; set; } = true;
    public string AudioTarget { get; set; } = "speaker";
    public DigitalSpectrum? AudioSpectrum { get; private set; }
    public string AudioAnalysisLabel { get; private set; } = "";
    public Audition? Audition { get; private set; }
    public string? AudioUrl { get; private set; }

    public WorkspaceViewModel(ProjectStore store, DownloadVault vault)
    {
        this.store = store;
        this.vault = vault;
        ResetDraft();
        savedFingerprint = ProjectCodec.Json(Project);
        RefreshLibrary();
    }

    public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void MarkDraft()
    {
        HasDraft = true;
        Notify();
    }

    public void ResetDraft()
    {
        Draft = ProjectCodec.Clone(Project);
        HasDraft = false;
    }

    public void DiscardDraft()
    {
        ResetDraft();
        Errors = [];
        Status = "已放弃未应用字段，项目保存状态未改变。";
        Notify();
    }

    public void Navigate(string tab)
    {
        Tab = tab;
        Notify();
    }

    public void SelectSpeaker(Guid id)
    {
        SelectedSpeakerId = id;
        ScanRequest.SpeakerId = id;
        AudioSpectrum = null;
        Preview = null;
        Notify();
    }

    public void SelectListener(Guid id)
    {
        SelectedListenerId = id;
        ClearAudio();
        Notify();
    }

    public void ApplyDraft()
    {
        Execute(() =>
        {
            RequireEditable();
            var candidate = ProjectCodec.Clone(Draft);
            var issues = Validation.Check(candidate);
            if (issues.Count > 0)
                throw new ArgumentException(string.Join("\n", issues));
            Commit(candidate, $"应用{Tab}编辑");
            Status = "字段已应用；涉及计算的输入更改后，请重新计算。";
        });
    }

    public void New(bool demo)
    {
        Execute(() =>
        {
            Project = demo ? Demonstration.Create() : new Project();
            diskHash = null;
            savedFingerprint = demo ? "" : ProjectCodec.Json(Project);
            undo.Clear();
            redo.Clear();
            Result = null;
            Comparison = null;
            Scan = null;
            SelectedSpeakerId = Project.Speakers.FirstOrDefault()?.Id;
            SelectedListenerId = Project.Listeners.FirstOrDefault()?.Id;
            ScanRequest.SpeakerId = SelectedSpeakerId ?? Guid.Empty;
            ResetAfterMutation();
            BaselineId = "";
            TargetId = "";
            Tab = "项目";
            Status = demo ? "已载入固定模拟示例；所有结果均为示例工程估算。" : "已建立空白项目。";
        });
    }

    public void CopyProject()
    {
        Execute(() =>
        {
            RequireApplied();
            var copy = ProjectCodec.Clone(Project);
            copy.Id = Guid.NewGuid();
            copy.Name = Project.Name.Length < 95 ? Project.Name + " 副本" : "项目副本";
            copy.Archived = false;
            copy.Created = DateTimeOffset.UtcNow;
            Commit(copy, "复制为独立项目标识");
            diskHash = null;
            Status = "已复制项目，请保存为独立文件。";
        });
    }

    public void ToggleArchive()
    {
        Execute(() =>
        {
            RequireApplied();
            var candidate = ProjectCodec.Clone(Project);
            candidate.Archived = !candidate.Archived;
            Commit(candidate, candidate.Archived ? "归档为只读" : "解除归档");
            Status = Project.Archived ? "项目已归档，编辑入口拒绝修改；仍可计算与导出。" : "已解除归档。";
        });
    }

    public void Undo()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (undo.Count == 0)
                return;
            redo.Push(ProjectCodec.Clone(Project));
            var prior = undo.Pop();
            PreserveAudit(prior, "撤销上一步");
            Project = prior;
            ResetAfterMutation();
            Status = "已撤销；审计保留，结果需重新核对。";
        });
    }

    public void Redo()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (redo.Count == 0)
                return;
            undo.Push(ProjectCodec.Clone(Project));
            var next = redo.Pop();
            PreserveAudit(next, "重做上一步");
            Project = next;
            ResetAfterMutation();
            Status = "已重做。";
        });
    }

    private void PreserveAudit(Project candidate, string action)
    {
        candidate.Revision = Project.Revision + 1;
        candidate.Audit = Project.Audit.TakeLast(999).ToList();
        candidate.Audit.Add(new AuditEntry { Revision = candidate.Revision, Action = action });
    }

    private void Commit(Project candidate, string action)
    {
        var issues = Validation.Check(candidate);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join("\n", issues));
        undo.Push(ProjectCodec.Clone(Project));
        if (undo.Count > 10)
        {
            var keep = undo.Take(10).Reverse().ToArray();
            undo.Clear();
            foreach (var item in keep)
                undo.Push(item);
        }
        redo.Clear();
        PreserveAudit(candidate, action);
        Project = candidate;
        ResetAfterMutation();
    }

    private void ResetAfterMutation()
    {
        ResetDraft();
        if (!Project.Speakers.Any(x => x.Id == SelectedSpeakerId))
            SelectedSpeakerId = Project.Speakers.FirstOrDefault()?.Id;
        if (!Project.Listeners.Any(x => x.Id == SelectedListenerId))
            SelectedListenerId = Project.Listeners.FirstOrDefault()?.Id;
        ScanRequest.SpeakerId = SelectedSpeakerId ?? Guid.Empty;
        Preview = null;
        AudioSpectrum = null;
        Comparison = null;
        Scan = null;
        Transect = null;
        ListenerPreview = null;
        ClearAudio();
        ClearDownload();
    }

    private void RequireEditable()
    {
        if (Project.Archived)
            throw new ArgumentException("当前项目为归档只读，请先在项目页解除归档");
    }

    private void RequireApplied()
    {
        if (HasDraft)
            throw new ArgumentException("存在未应用字段，请点击“应用编辑”或“放弃编辑”后继续");
    }

    public void Execute(Action action)
    {
        Errors = [];
        try
        {
            action();
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or FormatException or OverflowException)
        {
            Errors = error.Message.Split('\n').ToList();
            Status = "操作未完成，原有输入保留；请按提示修正。";
        }
        Notify();
    }

    public void Failure(string message)
    {
        Errors = [message];
        Status = "操作未完成，原有输入保留。";
        Notify();
    }

    private void ClearAudio()
    {
        Audition = null;
        AudioUrl = null;
    }

    private void ClearDownload()
    {
        LastDownload = null;
        LastFileName = null;
    }
}
