using System.Text.Json;
using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.Core.Operations;

public sealed class ProjectSession
{
    private readonly ProjectValidator validator = new();
    private readonly AnalysisEngine engine = new();
    private readonly List<HistoryFrame> undo = [];
    private readonly List<HistoryFrame> redo = [];
    public AcousticProject Project { get; private set; } = AcousticProject.Empty();
    public AnalysisReport Analysis { get; private set; }
    public bool Dirty { get; private set; }
    public string? SavedETag { get; private set; }
    public string SavedPath { get; private set; } = "";
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoLabel => undo.Count > 0 ? undo[^1].Action : "";
    public string RedoLabel => redo.Count > 0 ? redo[^1].Action : "";

    public ProjectSession()
    {
        Analysis = engine.Calculate(Project);
    }

    public void Open(AcousticProject project, string? etag = null, string path = "", bool changed = false)
    {
        validator.Ensure(project);
        var copy = ProjectCodec.Clone(project);
        var analysis = engine.Calculate(copy);
        Project = copy;
        Analysis = analysis;
        Dirty = changed;
        SavedETag = etag;
        SavedPath = path;
        undo.Clear();
        redo.Clear();
    }

    public void MarkSaved(SaveReceipt receipt)
    {
        SavedETag = receipt.ETag;
        SavedPath = receipt.Path;
        Dirty = false;
    }

    public void Apply(string action, string entity, Action<AcousticProject> change, bool allowArchived = false)
    {
        if (Project.Status == ProjectStatus.Archived && !allowArchived)
        {
            throw new DomainValidationException("status", "项目已归档，请先解除归档或创建副本");
        }

        var before = ProjectCodec.Clone(Project);
        var candidate = ProjectCodec.Clone(Project);
        change(candidate);
        var detail = DescribeChanges(before, candidate);
        Stamp(candidate, action, entity, detail);
        validator.Ensure(candidate);
        var analysis = engine.Calculate(candidate);
        undo.Add(new HistoryFrame(before, ProjectCodec.Clone(candidate), action));
        if (undo.Count > 20)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
        Project = candidate;
        Analysis = analysis;
        Dirty = true;
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var frame = undo[^1];
        RestoreHistory(frame.Before, "撤销：" + frame.Action);
        undo.RemoveAt(undo.Count - 1);
        redo.Add(frame);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var frame = redo[^1];
        RestoreHistory(frame.After, "重做：" + frame.Action);
        redo.RemoveAt(redo.Count - 1);
        undo.Add(frame);
    }

    public void MakeCopy()
    {
        var copy = ProjectCodec.Clone(Project);
        var original = copy.Id;
        copy.Id = Guid.NewGuid();
        copy.Name = (copy.Name.Length > 150 ? copy.Name[..150] : copy.Name) + " 副本";
        copy.Status = ProjectStatus.Active;
        Stamp(copy, "复制项目", copy.Id.ToString(), "来源项目=" + original + "；测量来源仍沿用原记录，不代表重新采集");
        Open(copy, changed: true);
    }

    public void RecordReview(string reviewer, string comment)
    {
        var snapshot = Analysis;
        var input = ProjectCodec.Fingerprint(Project);
        Apply("记录分析复核", Project.Id.ToString(), project =>
        {
            project.Reviews.Add(new ReviewRecord
            {
                InputRevision = project.Revision,
                Reviewer = reviewer.Trim(),
                Comment = comment.Trim(),
                ValidPairs = snapshot.ValidPairCount,
                BlockedPairs = snapshot.BlockedPairCount,
                BlockingReasons = snapshot.Pairs.SelectMany(pair => pair.BlockingReasons.Select(reason => pair.Title + "：" + reason)).ToList(),
                SettingsJson = JsonSerializer.Serialize(project.Settings, ProjectCodec.Options),
                InputHash = input
            });
        });
    }

    private void RestoreHistory(AcousticProject state, string action)
    {
        var candidate = ProjectCodec.Clone(state);
        candidate.Audit = ProjectCodec.Clone(Project.Audit);
        candidate.Reviews = ProjectCodec.Clone(Project.Reviews);
        candidate.Revision = Project.Revision;
        Stamp(candidate, action, candidate.Id.ToString(), "恢复数据状态，保留累计审计与复核；撤销记录仅限当前会话20步");
        validator.Ensure(candidate);
        var analysis = engine.Calculate(candidate);
        Project = candidate;
        Analysis = analysis;
        Dirty = true;
    }

    private static void Stamp(AcousticProject project, string action, string entity, string detail)
    {
        project.Revision++;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        project.SoftwareVersion = Software.Version;
        project.Audit.Add(new AuditEntry
        {
            Revision = project.Revision,
            Action = action,
            Entity = entity,
            Detail = detail.Length > 16000 ? detail[..16000] : detail,
            InputHash = ProjectCodec.Fingerprint(project)
        });
        if (project.Audit.Count > 10000)
        {
            project.Audit.RemoveRange(0, project.Audit.Count - 10000);
        }
    }

    private static string DescribeChanges(AcousticProject before, AcousticProject after)
    {
        using var left = JsonDocument.Parse(JsonSerializer.Serialize(before, ProjectCodec.Options));
        using var right = JsonDocument.Parse(JsonSerializer.Serialize(after, ProjectCodec.Options));
        var changes = new List<string>();
        foreach (var property in right.RootElement.EnumerateObject())
        {
            if (property.Name is "audit" or "reviews")
            {
                continue;
            }

            if (!left.RootElement.TryGetProperty(property.Name, out var previous) || previous.GetRawText() != property.Value.GetRawText())
            {
                changes.Add(property.Name);
            }
        }

        return changes.Count == 0 ? "记录复核或状态操作" : "修改字段/集合：" + string.Join("、", changes);
    }

    private sealed record HistoryFrame(AcousticProject Before, AcousticProject After, string Action);
}
