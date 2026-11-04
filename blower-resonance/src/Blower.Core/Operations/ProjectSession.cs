using Blower.Core.Domain;
using Blower.Core.IO;

namespace Blower.Core.Operations;

public sealed class ProjectSession
{
    private readonly Stack<Project> undo = new();
    private readonly Stack<Project> redo = new();
    public Project Current { get; private set; } = new();
    public string SavedHash { get; private set; } = "";
    public string StoreToken { get; private set; } = "";
    public bool CanUndo => undo.Count > 0 && !Current.Archived;
    public bool CanRedo => redo.Count > 0 && !Current.Archived;
    public bool Dirty => SavedHash != ProjectCodec.Hash(System.Text.Json.JsonSerializer.Serialize(Current, ProjectCodec.Options));

    public void Load(Project project, string storeToken = "", bool saved = false)
    {
        Validation.RequireValid(project);
        Current = ProjectCodec.Clone(project);
        undo.Clear();
        redo.Clear();
        StoreToken = storeToken;
        SavedHash = saved ? ProjectCodec.Hash(ProjectCodec.Encode(Current)) : "";
    }

    public void Change(string action, Action<Project> change, bool analysisChange = true, bool allowArchived = false)
    {
        if (Current.Archived && !allowArchived)
        {
            throw new InvalidOperationException("项目已归档；先解除归档");
        }
        var candidate = ProjectCodec.Clone(Current);
        change(candidate);
        if (analysisChange)
        {
            ProjectCommands.MarkChecksForRecheck(candidate);
        }
        candidate.Revision++;
        candidate.Modified = DateTimeOffset.UtcNow;
        candidate.Audit.Add(new()
        {
            Action = action,
            Detail = "已通过完整项目校验" + (analysisChange ? "；计算输入变化，已完成验证标为需复核" : ""),
            Revision = candidate.Revision
        });
        Validation.RequireValid(candidate);
        if (undo.Count >= 30)
        {
            var recent = undo.Take(29).Reverse().ToList();
            undo.Clear();
            foreach (var state in recent)
            {
                undo.Push(state);
            }
        }
        undo.Push(ProjectCodec.Clone(Current));
        redo.Clear();
        Current = candidate;
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            throw new InvalidOperationException("没有可撤销操作或项目已归档");
        }
        redo.Push(ProjectCodec.Clone(Current));
        Restore(undo.Pop(), "撤销");
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            throw new InvalidOperationException("没有可重做操作或项目已归档");
        }
        undo.Push(ProjectCodec.Clone(Current));
        Restore(redo.Pop(), "重做");
    }

    private void Restore(Project restored, string action)
    {
        var audit = Current.Audit.ToList();
        var revision = Current.Revision + 1;
        restored.Revision = revision;
        restored.Audit = audit;
        restored.Modified = DateTimeOffset.UtcNow;
        restored.Audit.Add(new() { Action = action, Detail = "恢复输入快照；保留累计审计", Revision = revision });
        ProjectCommands.MarkChecksForRecheck(restored);
        Current = restored;
    }

    public SavedProject Save(ProjectStore store)
    {
        var result = store.Save(Current, StoreToken);
        StoreToken = result.Token;
        SavedHash = ProjectCodec.Hash(ProjectCodec.Encode(Current));
        return result;
    }
}
