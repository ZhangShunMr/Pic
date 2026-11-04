using System.IO;
using TrimContact.Core.IO;

namespace TrimContact.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private ProjectStore? library;
    private IReadOnlyList<StoredProject> libraryRows = [];
    private StoredProject? selectedLibraryItem;
    public string LibraryDirectory { get; }
    private ProjectStore Library => library ??= new ProjectStore(LibraryDirectory);
    public IReadOnlyList<StoredProject> LibraryRows => libraryRows;
    public StoredProject? SelectedLibraryItem
    {
        get => selectedLibraryItem;
        set { if (Set(ref selectedLibraryItem, value)) RefreshCommands(); }
    }
    public RelayCommand RefreshLibraryCommand { get; private set; } = null!;
    public RelayCommand SaveLibraryCommand { get; private set; } = null!;
    public RelayCommand OpenLibraryCommand { get; private set; } = null!;
    public RelayCommand RecoverLibraryCommand { get; private set; } = null!;

    private void InitializeLibraryCommands()
    {
        RefreshLibraryCommand = Command(RefreshLibrary, () => !IsBusy);
        SaveLibraryCommand = Command(() =>
        {
            ApplyDraft();
            var target = Library.FilePath(Applied.Id);
            var same = session.SavedPath is not null && string.Equals(Path.GetFullPath(session.SavedPath),
                Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            var saved = Library.Save(Applied, same ? session.DiskHash : null);
            session.Saved(saved);
            Raise(nameof(FileLocation));
            NotifyDraftEdited();
            RefreshLibrary();
            Status = "已存入本机项目库：" + saved.Path;
        }, () => !IsBusy && !HasConversionErrors);
        OpenLibraryCommand = Command(() => OpenLibrary(false),
            () => !IsBusy && SelectedLibraryItem is not null && SelectedLibraryItem.Error is null);
        RecoverLibraryCommand = Command(() => OpenLibrary(true),
            () => !IsBusy && SelectedLibraryItem is not null);
    }

    private void RefreshLibrary()
    {
        var id = SelectedLibraryItem?.Id;
        libraryRows = Library.List();
        Raise(nameof(LibraryRows));
        SelectedLibraryItem = libraryRows.FirstOrDefault(p => p.Id == id) ?? libraryRows.FirstOrDefault();
        Status = "本机项目库已刷新，共" + libraryRows.Count + "项。";
    }

    private void OpenLibrary(bool backup)
    {
        if (SelectedLibraryItem is null) return;
        var id = SelectedLibraryItem.Id;
        if (!ConfirmLeave()) return;
        var opened = Library.Open(id, backup);
        session.Load(opened.Project, opened.Hash, backup ? Library.FilePath(id) : opened.Path);
        if (backup)
        {
            session.CopyProject();
            Status = "已把备份复制为新项目，原磁盘项目保持原状。请保存恢复副本。";
        }
        else Status = "已打开本机项目库条目，请重新计算结果。";
        report = null;
        sweepRows = [];
        ReloadDraft();
    }
}
