using System.IO;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public RelayCommand NewCommand { get; private set; } = null!;
    public RelayCommand OpenCommand { get; private set; } = null!;
    public RelayCommand SaveCommand { get; private set; } = null!;
    public RelayCommand SaveAsCommand { get; private set; } = null!;
    public RelayCommand DemoCommand { get; private set; } = null!;
    public RelayCommand CopyProjectCommand { get; private set; } = null!;
    public RelayCommand ArchiveCommand { get; private set; } = null!;
    public RelayCommand ApplyCommand { get; private set; } = null!;
    public RelayCommand DiscardDraftCommand { get; private set; } = null!;
    public RelayCommand UndoCommand { get; private set; } = null!;
    public RelayCommand RedoCommand { get; private set; } = null!;
    public RelayCommand RectangleCommand { get; private set; } = null!;
    public RelayCommand AddVertexCommand { get; private set; } = null!;
    public RelayCommand DeleteVertexCommand { get; private set; } = null!;
    public RelayCommand AddVariantCommand { get; private set; } = null!;
    public RelayCommand CopyVariantCommand { get; private set; } = null!;
    public RelayCommand DeleteVariantCommand { get; private set; } = null!;
    public RelayCommand AddClipCommand { get; private set; } = null!;
    public RelayCommand DeleteClipCommand { get; private set; } = null!;
    public RelayCommand AddContactCommand { get; private set; } = null!;
    public RelayCommand DeleteContactCommand { get; private set; } = null!;
    public RelayCommand AddLoadCommand { get; private set; } = null!;
    public RelayCommand DeleteLoadCommand { get; private set; } = null!;
    public RelayCommand AddTaskCommand { get; private set; } = null!;
    public RelayCommand DeleteTaskCommand { get; private set; } = null!;
    public RelayCommand GenerateTasksCommand { get; private set; } = null!;
    public RelayCommand CompareFileCommand { get; private set; } = null!;
    public RelayCommand PreviewImportCommand { get; private set; } = null!;
    public RelayCommand ApplyImportCommand { get; private set; } = null!;
    public RelayCommand ReadCsvCommand { get; private set; } = null!;
    public RelayCommand CsvTemplateCommand { get; private set; } = null!;
    public RelayCommand ExportLayoutCommand { get; private set; } = null!;
    public RelayCommand PreviewBatchCommand { get; private set; } = null!;
    public RelayCommand ApplyBatchCommand { get; private set; } = null!;
    public RelayCommand CancelCommand { get; private set; } = null!;
    public RelayCommand ExportReportCommand { get; private set; } = null!;
    public RelayCommand ExportCsvCommand { get; private set; } = null!;
    public RelayCommand ExportTraceCommand { get; private set; } = null!;
    public RelayCommand ExportJsonCommand { get; private set; } = null!;
    public RelayCommand ExportBalanceCommand { get; private set; } = null!;
    public RelayCommand ExportTasksCommand { get; private set; } = null!;
    public RelayCommand ExportSweepCommand { get; private set; } = null!;
    public AsyncCommand CalculateCommand { get; private set; } = null!;
    public AsyncCommand SweepCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        NewCommand = Command(() => ReplaceProject(new DesignProject()), () => !IsBusy);
        DemoCommand = Command(() =>
        {
            if (interaction.Confirm("载入400×300mm饰板、四卡扣和中心缓冲块模拟示例。所有参数均为模拟，不证明实车效果。"))
                ReplaceProject(Demonstration.Create());
        }, () => !IsBusy);
        OpenCommand = Command(OpenProject, () => !IsBusy);
        SaveCommand = Command(() => SaveProject(false), () => !IsBusy && !HasConversionErrors);
        SaveAsCommand = Command(() => SaveProject(true), () => !IsBusy && !HasConversionErrors);
        CopyProjectCommand = Command(() =>
        {
            ApplyDraft();
            if (!ConfirmLeave()) return;
            session.CopyProject();
            report = null;
            ReloadDraft();
            Status = "已复制为新项目ID，请保存。";
        }, () => !IsBusy && !HasConversionErrors);
        ArchiveCommand = Command(() =>
        {
            ApplyDraft();
            if (!interaction.Confirm("将项目归档为只读。可复制项目后继续编辑。")) return;
            session.Edit("归档项目", p => p.Archived = true);
            ReloadDraft();
        }, () => CanEdit && !HasConversionErrors);
        ApplyCommand = Command(ApplyDraft, () => CanEdit && !HasConversionErrors);
        DiscardDraftCommand = Command(() =>
        {
            if (interaction.Confirm("放弃尚未应用的输入并恢复工作副本？")) ReloadDraft();
        }, () => !IsBusy);
        UndoCommand = Command(() =>
        {
            if (!ConfirmDiscardDraft()) return;
            session.Undo();
            ReloadDraft();
            Status = "已撤销上次应用。";
        }, () => !IsBusy && session.CanUndo);
        RedoCommand = Command(() =>
        {
            if (!ConfirmDiscardDraft()) return;
            session.Redo();
            ReloadDraft();
            Status = "已重做。";
        }, () => !IsBusy && session.CanRedo);
        RectangleCommand = Command(CreateRectangle, () => CanEdit && !HasConversionErrors);
        AddVertexCommand = Command(() =>
        {
            if (Draft.Panel.Outline.Count >= 24) throw new InvalidDataException("轮廓最多24个顶点");
            var vertex = new Vertex { Xmm = PlacementX, Ymm = PlacementY };
            Draft.Panel.Outline.Add(vertex);
            RefreshCollections();
            SelectedVertex = vertex;
        }, () => CanEdit && !HasConversionErrors);
        DeleteVertexCommand = Command(() =>
        {
            if (SelectedVertex is null) return;
            Draft.Panel.Outline.Remove(SelectedVertex);
            SelectedVertex = Draft.Panel.Outline.FirstOrDefault();
            RefreshCollections();
        }, () => CanEdit && !HasConversionErrors && SelectedVertex is not null);
        AddVariantCommand = Command(AddVariant, () => CanEdit && !HasConversionErrors);
        CopyVariantCommand = Command(CopyVariant, () => CanEdit && !HasConversionErrors && SelectedVariant is not null);
        DeleteVariantCommand = Command(DeleteVariant, () => CanEdit && !HasConversionErrors && SelectedVariant is not null);
        AddClipCommand = Command(AddClip, () => CanEdit && !HasConversionErrors && SelectedVariant is not null && !HasConversionErrors);
        AddContactCommand = Command(AddContact, () => CanEdit && !HasConversionErrors && SelectedVariant is not null && !HasConversionErrors);
        DeleteClipCommand = Command(DeleteClip, () => CanEdit && !HasConversionErrors && SelectedClip is not null);
        DeleteContactCommand = Command(DeleteContact, () => CanEdit && !HasConversionErrors && SelectedContact is not null);
        AddLoadCommand = Command(() =>
        {
            if (Draft.Loads.Count >= 6) throw new InvalidDataException("最多6个载荷工况");
            var load = new LoadCase { Name = "载荷 " + (Draft.Loads.Count + 1) };
            Draft.Loads.Add(load);
            RefreshCollections();
            SelectedLoad = load;
        }, () => CanEdit && !HasConversionErrors);
        DeleteLoadCommand = Command(() =>
        {
            if (SelectedLoad is null || !interaction.Confirm("删除选中载荷工况？")) return;
            Draft.Loads.Remove(SelectedLoad);
            SelectedLoad = Draft.Loads.FirstOrDefault();
            RefreshCollections();
        }, () => CanEdit && !HasConversionErrors && SelectedLoad is not null);
        AddTaskCommand = Command(() =>
        {
            if (Draft.Tasks.Count >= 200) throw new InvalidDataException("最多200个验证任务");
            var task = new VerificationTask();
            Draft.Tasks.Add(task);
            RefreshCollections();
            SelectedTask = task;
        }, () => CanEdit && !HasConversionErrors);
        DeleteTaskCommand = Command(() =>
        {
            if (SelectedTask is null || !interaction.Confirm("删除选中验证任务及其文字证据？")) return;
            Draft.Tasks.Remove(SelectedTask);
            SelectedTask = Draft.Tasks.FirstOrDefault();
            RefreshCollections();
        }, () => CanEdit && !HasConversionErrors && SelectedTask is not null);
        InitializeAnalysisCommands();
        InitializeExchangeCommands();
        InitializeLibraryCommands();
    }

    private static void ReplaceValues(DesignProject target, DesignProject source)
    {
        target.Name = source.Name;
        target.Revision = source.Revision;
        target.Owner = source.Owner;
        target.Goal = source.Goal;
        target.Source = source.Source;
        target.Origin = source.Origin;
        target.Panel = source.Panel;
        target.Settings = source.Settings;
        target.Variants = source.Variants;
        target.Loads = source.Loads;
        target.Tasks = source.Tasks;
    }

    private void RequireNoConversionErrors()
    {
        if (HasConversionErrors)
            throw new InvalidOperationException("存在数值格式错误，请修正红框字段，或放弃草稿后重试。");
    }

    private void ApplyDraft()
    {
        RequireNoConversionErrors();
        if (!DraftDirty) return;
        ProjectValidation.Require(Draft);
        var candidate = ProjectCodec.Clone(Draft);
        session.Edit("应用桌面编辑", p => ReplaceValues(p, candidate));
        ReloadDraft();
        Status = "草稿已应用。输入改变后须重新计算；任务与名称编辑不改变物理输入哈希。";
    }

    private bool ConfirmDiscardDraft() => (!DraftDirty && !HasConversionErrors)
        || interaction.Confirm("当前还有未应用输入，放弃这些输入后继续？");

    public bool RequestClose()
    {
        if (IsBusy)
        {
            interaction.Information("计算正在运行，请先取消计算，待任务结束后关闭。");
            return false;
        }
        return ConfirmLeave();
    }

    private bool ConfirmLeave()
    {
        if (!HasUnsavedChanges) return true;
        switch (interaction.Unsaved("当前项目有未保存内容，是否先保存？"))
        {
            case UnsavedChoice.Discard: return true;
            case UnsavedChoice.Cancel: return false;
            default:
                bool saved = false;
                Guard(() => saved = SaveProject(false));
                return saved;
        }
    }

    private void ReplaceProject(DesignProject project)
    {
        if (!ConfirmLeave()) return;
        session.Load(project);
        if (project.Origin == DataOrigin.Simulation)
            session.Edit("建立独立模拟工作副本", _ => { });
        report = null;
        sweepRows = [];
        ReloadDraft();
        Status = project.Origin == DataOrigin.Simulation ? "已载入独立模拟示例。请另存文件。" : "已建立空白项目。";
    }

    private void OpenProject()
    {
        var path = interaction.OpenPath("接触设计项目 (*.trimproj;*.bak)|*.trimproj;*.bak|所有文件|*.*");
        if (path is null) return;
        if (!ConfirmLeave()) return;
        var opened = DesktopFiles.Open(path);
        session.Load(opened.Project, opened.Hash, opened.Path);
        report = null;
        sweepRows = [];
        ReloadDraft();
        Status = "已打开 " + path + "。结果不随项目储存，请重新计算。";
    }

    private bool SaveProject(bool saveAs)
    {
        ApplyDraft();
        var path = saveAs ? null : session.SavedPath;
        var suggested = string.Concat(Applied.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        path ??= interaction.SavePath("接触设计项目 (*.trimproj)|*.trimproj", suggested + ".trimproj");
        if (path is null) return false;
        var samePath = session.SavedPath is not null
            && string.Equals(Path.GetFullPath(path), Path.GetFullPath(session.SavedPath), StringComparison.OrdinalIgnoreCase);
        var saved = DesktopFiles.Save(path, Applied, samePath ? session.DiskHash : null, overwriteConfirmed: true);
        session.Saved(saved);
        Raise(nameof(FileLocation));
        NotifyDraftEdited();
        Status = "保存成功：" + saved.Path;
        return true;
    }

    private void CreateRectangle()
    {
        if (!double.IsFinite(RectangleWidth) || !double.IsFinite(RectangleHeight)
            || RectangleWidth is < 1 or > 10000 || RectangleHeight is < 1 or > 10000)
            throw new InvalidDataException("矩形宽高须为1至10000mm的有限数值");
        if (Draft.Panel.Outline.Count > 0 && !interaction.Confirm("用矩形替换当前轮廓并把质心移至中心？点位不会自动移动。")) return;
        Draft.Panel.Outline =
        [
            new() { Xmm = 0, Ymm = 0 },
            new() { Xmm = RectangleWidth, Ymm = 0 },
            new() { Xmm = RectangleWidth, Ymm = RectangleHeight },
            new() { Xmm = 0, Ymm = RectangleHeight }
        ];
        Draft.Panel.CenterXmm = RectangleWidth / 2;
        Draft.Panel.CenterYmm = RectangleHeight / 2;
        PlacementX = Draft.Panel.CenterXmm;
        PlacementY = Draft.Panel.CenterYmm;
        SelectedVertex = Draft.Panel.Outline[0];
        RefreshCollections();
        Raise(nameof(PlacementX));
        Raise(nameof(PlacementY));
        Status = "矩形轮廓已放入草稿。确认已有点位仍位于新轮廓内，再应用编辑。";
    }

    private void RefreshCollections()
    {
        Raise(nameof(Draft));
        foreach (var name in new[] { nameof(Variants), nameof(Vertices), nameof(Clips), nameof(Contacts), nameof(Loads), nameof(Tasks) }) Raise(name);
        Raise(nameof(SelectedVariant));
        Raise(nameof(SelectedVariantSummary));
        NotifyDraftEdited();
    }

    private void AddVariant()
    {
        if (Draft.Variants.Count >= 8) throw new InvalidDataException("最多8个方案");
        var variant = new DesignVariant { Name = "方案 " + (Draft.Variants.Count + 1) };
        Draft.Variants.Add(variant);
        RefreshCollections();
        SelectedVariant = variant;
        Status = "新方案已建立。计算至少需要3个非共线卡扣，可在布置图点击设置新增位置。";
    }

    private void CopyVariant()
    {
        if (SelectedVariant is null) return;
        if (Draft.Variants.Count >= 8) throw new InvalidDataException("最多8个方案");
        var copy = DesignOperations.CopyVariant(SelectedVariant, Limited(SelectedVariant.Name + " 副本", 100));
        Draft.Variants.Add(copy);
        RefreshCollections();
        SelectedVariant = copy;
        Status = "已复制方案和点位，新点位使用独立ID。请修改参数、应用后重算。";
    }

    private void DeleteVariant()
    {
        if (SelectedVariant is null || !interaction.Confirm("删除选中方案和全部点位？关联验证任务会保留并解除引用。")) return;
        DesignOperations.RemoveVariant(Draft, SelectedVariant.Id);
        RefreshCollections();
        SelectedVariant = Draft.Variants.FirstOrDefault();
    }

    public void PlaceAt(double x, double y, Guid? pointId)
    {
        if (!CanEdit) return;
        PlacementX = Math.Round(x, 2);
        PlacementY = Math.Round(y, 2);
        Raise(nameof(PlacementX));
        Raise(nameof(PlacementY));
        if (pointId.HasValue && SelectedVariant is not null)
        {
            var clip = SelectedVariant.Clips.FirstOrDefault(c => c.Id == pointId);
            var contact = SelectedVariant.Contacts.FirstOrDefault(c => c.Id == pointId);
            if (clip is not null) SelectedClip = clip;
            if (contact is not null) SelectedContact = contact;
        }
        Status = $"新增位置 ({N(PlacementX)}, {N(PlacementY)}) mm。点击添加卡扣或添加接触点。";
    }

    private void AddClip()
    {
        if (SelectedVariant is null) return;
        if (SelectedVariant.Clips.Count >= 12) throw new InvalidDataException("每方案最多12个卡扣");
        var clip = new Clip { Name = "卡扣 " + (SelectedVariant.Clips.Count + 1), Xmm = PlacementX, Ymm = PlacementY };
        SelectedVariant.Clips.Add(clip);
        RefreshCollections();
        SelectedClip = clip;
    }

    private void AddContact()
    {
        if (SelectedVariant is null) return;
        if (SelectedVariant.Contacts.Count >= 6) throw new InvalidDataException("每方案最多6个接触点");
        var contact = new ContactPoint { Name = "接触点 " + (SelectedVariant.Contacts.Count + 1), Xmm = PlacementX, Ymm = PlacementY };
        SelectedVariant.Contacts.Add(contact);
        RefreshCollections();
        SelectedContact = contact;
    }

    private void DeleteClip()
    {
        if (SelectedVariant is null || SelectedClip is null || !interaction.Confirm("删除选中卡扣？关联任务将保留并解除引用。")) return;
        DesignOperations.DetachTasks(Draft, [SelectedClip.Id]);
        SelectedVariant.Clips.Remove(SelectedClip);
        SelectedClip = SelectedVariant.Clips.FirstOrDefault();
        RefreshCollections();
    }

    private void DeleteContact()
    {
        if (SelectedVariant is null || SelectedContact is null || !interaction.Confirm("删除选中接触点？关联任务将保留并解除引用。")) return;
        DesignOperations.DetachTasks(Draft, [SelectedContact.Id]);
        SelectedVariant.Contacts.Remove(SelectedContact);
        SelectedContact = SelectedVariant.Contacts.FirstOrDefault();
        RefreshCollections();
    }
}
