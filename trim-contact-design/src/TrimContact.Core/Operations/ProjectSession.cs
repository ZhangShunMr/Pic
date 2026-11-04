using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Operations;

public sealed class ProjectSession
{
    private readonly Stack<DesignProject> undo = new();
    private readonly Stack<DesignProject> redo = new();
    public DesignProject Current { get; private set; } = new();
    public string? DiskHash { get; private set; }
    public string? SavedPath { get; private set; }
    private string savedState = "";
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool Dirty => JsonState(Current) != savedState;

    public ProjectSession()
    {
        savedState = JsonState(Current);
    }

    private static string JsonState(DesignProject p) => System.Text.Json.JsonSerializer.Serialize(p, ProjectCodec.Options);

    public void Load(DesignProject project, string? hash = null, string? path = null)
    {
        ProjectValidation.Require(project);
        Current = ProjectCodec.Clone(project);
        DiskHash = hash;
        SavedPath = path;
        undo.Clear();
        redo.Clear();
        savedState = JsonState(Current);
    }

    public void Edit(string action, Action<DesignProject> change)
    {
        if (Current.Archived)
            throw new InvalidOperationException("归档项目为只读，请复制项目后继续设计");
        var next = ProjectCodec.Clone(Current);
        change(next);
        ProjectValidation.Require(next);
        next.ModifiedUtc = DateTimeOffset.UtcNow;
        next.Audit.Add(new AuditEntry { Action = action });
        if (next.Audit.Count > 500)
            next.Audit.RemoveRange(0, next.Audit.Count - 500);
        undo.Push(ProjectCodec.Clone(Current));
        if (undo.Count > 20)
        {
            var keep = undo.Take(20).Reverse().ToArray();
            undo.Clear();
            foreach (var item in keep)
                undo.Push(item);
        }
        Current = next;
        redo.Clear();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        redo.Push(Current);
        Current = undo.Pop();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;
        undo.Push(Current);
        Current = redo.Pop();
    }

    public void Saved(OpenedProject result)
    {
        DiskHash = result.Hash;
        SavedPath = result.Path;
        savedState = JsonState(Current);
    }

    public void CopyProject()
    {
        var copy = ProjectCodec.Clone(Current);
        copy.Id = Guid.NewGuid();
        copy.Name += " 副本";
        if (copy.Name.Length > 100)
            copy.Name = copy.Name[..100];
        copy.Archived = false;
        copy.CreatedUtc = DateTimeOffset.UtcNow;
        copy.ModifiedUtc = copy.CreatedUtc;
        copy.Audit.Add(new AuditEntry { Action = "从项目 " + Current.Id + " 复制" });
        if (copy.Audit.Count > 500)
            copy.Audit.RemoveAt(0);
        Load(copy);
        savedState = "";
    }
}
