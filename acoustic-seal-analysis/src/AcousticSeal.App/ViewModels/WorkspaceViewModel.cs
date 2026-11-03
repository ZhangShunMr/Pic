using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.Core.Operations;

namespace AcousticSeal.App.ViewModels;

public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly ProjectStore store;
    private readonly DownloadVault downloads;
    private readonly CsvImportService csv = new();
    private readonly BatchPairing pairing = new();
    private readonly ResultExport export = new();
    private Func<Task>? confirmedAction;
    private Guid? selectedLocation;
    private Guid? selectedCondition;
    private Guid? selectedTrial;
    private Guid? selectedPair;
    public ProjectSession Session { get; } = new();
    public AcousticProject Project => Session.Project;
    public AnalysisReport Analysis => Session.Analysis;
    public string Page { get; private set; } = "project";
    public WorkspacePage CurrentPage => Navigation.Get(Page);
    public bool Busy { get; private set; }
    public UserNotice? Notice { get; private set; }
    public List<ValidationMessage> Problems { get; private set; } = [];
    public string Confirmation { get; private set; } = "";
    public string ConfirmationTitle { get; private set; } = "确认操作";
    public List<ProjectCard> Library { get; private set; } = [];
    public string StoreDirectory => store.DirectoryPath;
    public string LastSavedPath { get; private set; } = "";
    public string LastBackupPath { get; private set; } = "";
    public string DownloadUrl { get; private set; } = "";
    public string DownloadFilename { get; private set; } = "";
    public List<string> MigrationNotes { get; private set; } = [];
    public string TrialSearch { get; set; } = "";
    public TrialKind? TrialFilter { get; set; }
    public int TrialPage { get; private set; }
    public const int PageSize = 40;
    public string QualitySeverity { get; set; } = "all";
    public bool ReviewDraftDirty { get; private set; }
    public string Reviewer { get; set; } = "";
    public string ReviewComment { get; set; } = "";
    public MetadataEditor Metadata { get; private set; }
    public LocationEditor Location { get; private set; } = new();
    public ConditionEditor Condition { get; private set; } = new();
    public TrialEditor Trial { get; private set; } = new();
    public PairEditor Pair { get; private set; } = new();
    public SettingsEditor Settings { get; private set; }
    public ImportEditor Import { get; private set; } = new();
    public BatchPairEditor Batch { get; private set; } = new();
    public AnalysisFilterViewModel Filter { get; } = new();
    public RevisionViewModel Revision { get; } = new();
    public WorkbookViewModel Workbook { get; } = new();
    public ScenarioViewModel Scenario { get; } = new();

    public void ExportScenario()
    {
        Scenario.Export(Project, downloads);
    }

    public void ExportWorkbook()
    {
        Workbook.Export(Project, downloads);
    }

    public void ExportRevision()
    {
        Revision.Export(downloads);
    }
    public bool Archived => Project.Status == ProjectStatus.Archived;
    public bool HasDraft => ActiveEditor()?.IsDirty == true || (Page == "pairs" && Batch.IsDirty) || ReviewDraftDirty;
    public bool HasUnsaved => Session.Dirty || HasDraft;

    public WorkspaceViewModel(ProjectStore store, DownloadVault downloads)
    {
        this.store = store;
        this.downloads = downloads;
        Metadata = Track(new MetadataEditor(Project));
        Settings = Track(new SettingsEditor(Project.Settings));
        Import = Track(Import);
        Batch = Track(Batch);
        Trial = Track(Trial);
        Location = Track(Location);
        Condition = Track(Condition);
        Pair = Track(Pair);
        Revision.Changed += () => Notify();
        Workbook.Changed += () => Notify();
        Scenario.Changed += () => Notify();
        Filter.Changed += () => Notify();
    }

    public async Task InitializeAsync()
    {
        await RunAsync(async () => Library = await store.ListAsync());
    }

    public void MarkEditorDirty()
    {
        ActiveEditor()?.MarkDirty();
        Notify(nameof(HasDraft));
    }

    public void Navigate(string page)
    {
        AskDraft("切换工作页将放弃尚未应用的表单输入。", () =>
        {
            if (Page == "import" && Import.IsDirty)
            {
                Import = Track(new ImportEditor());
            }

            if (Page == "pairs" && Batch.IsDirty)
            {
                Batch = Track(new BatchPairEditor());
            }

            if (ReviewDraftDirty)
            {
                Reviewer = "";
                ReviewComment = "";
                ReviewDraftDirty = false;
            }

            ReloadEditors();
            Page = Navigation.Get(page).Key;
            Notify();
        });
    }

    public void SelectLocation(Guid? id)
    {
        AskDraft("切换位置将放弃尚未应用的输入。", () =>
        {
            selectedLocation = id;
            Location = Track(new LocationEditor(Project.Locations.FirstOrDefault(item => item.Id == id)));
            Notify();
        });
    }

    public void SelectCondition(Guid? id)
    {
        AskDraft("切换工况将放弃尚未应用的输入。", () =>
        {
            selectedCondition = id;
            Condition = Track(new ConditionEditor(Project.Conditions.FirstOrDefault(item => item.Id == id)));
            Notify();
        });
    }

    public void SelectTrial(Guid? id)
    {
        AskDraft("切换试验将放弃尚未应用的输入。", () =>
        {
            selectedTrial = id;
            Trial = Track(new TrialEditor(Project.Trials.FirstOrDefault(item => item.Id == id), Project.Conditions.FirstOrDefault()?.Id));
            Notify();
        });
    }

    public void SelectPair(Guid? id)
    {
        AskDraft("切换配对将放弃尚未应用的输入。", () =>
        {
            selectedPair = id;
            Pair = Track(new PairEditor(Project.Pairs.FirstOrDefault(item => item.Id == id)));
            Notify();
        });
    }

    public void NewProject()
    {
        AskReplace("建立空白项目", () =>
        {
            Session.Open(AcousticProject.Empty());
            ResetWorkspace();
            Message("已建立空白项目，请填写目标并应用。");
        });
    }

    public void LoadDemo()
    {
        Ask("载入模拟案例", "将载入2个位置、1个完整工况、8个试验和6条配对。全部数据模拟，不能证明真实应用效果。" +
            (HasUnsaved ? "当前有未保存内容，确认前请取消并先保存。" : ""), () =>
        {
            Session.Open(Demonstration.Create(), changed: true);
            ResetWorkspace();
            Page = "analysis";
            Message("固定模拟案例已载入，来源标签随界面和输出保留。");
        });
    }

    public void CloneProject()
    {
        AskDraft("复制前将放弃未应用表单，只复制已应用的数据。", () => Run(() =>
        {
            Session.MakeCopy();
            ResetWorkspace();
            Message("已创建独立项目副本；保留原始测量来源，不表示重新采集。");
        }));
    }

    public void SaveMetadata()
    {
        Run(() =>
        {
            Session.Apply("应用项目信息", Project.Id.ToString(), Metadata.ApplyTo);
            AfterChange("项目信息已应用。", reload: true);
        });
    }

    public void SaveLocation()
    {
        Run(() =>
        {
            var item = Location.ToModel();
            ProjectCommands.SaveLocation(Session, item);
            selectedLocation = item.Id;
            AfterChange("位置与工艺已应用。");
        });
    }

    public void SaveCondition()
    {
        Run(() =>
        {
            var item = Condition.ToModel();
            ProjectCommands.SaveCondition(Session, item);
            selectedCondition = item.Id;
            AfterChange("工况已应用，全部相关结果重新计算。");
        });
    }

    public void SaveTrial()
    {
        Run(() =>
        {
            var item = Trial.ToModel();
            ProjectCommands.SaveTrial(Session, item);
            selectedTrial = item.Id;
            AfterChange("试验与频带已应用。");
        });
    }

    public void SavePair()
    {
        Run(() =>
        {
            var item = Pair.ToModel();
            ProjectCommands.SavePair(Session, item);
            selectedPair = item.Id;
            AfterChange("显式配对已应用；不可比时保留原因并阻断差值。");
        });
    }

    public void SaveSettings()
    {
        Run(() =>
        {
            ProjectCommands.SaveSettings(Session, Settings.ToModel());
            AfterChange("参数已应用，所有历史输入按新设置重新计算。");
        });
    }

    public void DeleteSelected(string kind, Guid id)
    {
        Ask("删除对象", "删除前检查引用。若仍有试验或配对引用，软件会阻止删除。确定继续？", () => Run(() =>
        {
            switch (kind)
            {
                case "location":
                    ProjectCommands.DeleteLocation(Session, id);
                    selectedLocation = null;
                    break;
                case "condition":
                    ProjectCommands.DeleteCondition(Session, id);
                    selectedCondition = null;
                    break;
                case "trial":
                    ProjectCommands.DeleteTrial(Session, id);
                    selectedTrial = null;
                    break;
                case "pair":
                    ProjectCommands.DeletePair(Session, id);
                    selectedPair = null;
                    break;
                default:
                    throw new DomainValidationException("delete", "未知对象类型");
            }

            AfterChange("对象已删除，可在当前会话撤销。");
        }));
    }

    public void DuplicateTrial()
    {
        AskDraft("复制只采用已应用的试验结构，未应用输入将放弃。", () => Run(() =>
        {
            selectedTrial = ProjectCommands.DuplicateTrial(Session, Trial.Id);
            AfterChange("已复制结构，测量、重复组和配对未复制，请复核时间来源。");
        }));
    }

    public void ToggleArchive()
    {
        if (HasDraft)
        {
            Message("请先应用或取消表单，再改变归档状态。", true);
            return;
        }

        Ask(Archived ? "解除归档" : "归档为只读", "归档项目仍可保存导出；解除后可继续编辑。", () => Run(() =>
        {
            ProjectCommands.SetArchived(Session, !Archived);
            AfterChange(Archived ? "项目已归档。" : "已解除归档。");
        }));
    }

    public void Undo()
    {
        AskDraft("撤销前将放弃未应用的表单输入。", () => Run(() =>
        {
            Session.Undo();
            AfterChange("已撤销数据修改，累计审计与复核保留。");
        }));
    }

    public void Redo()
    {
        AskDraft("重做前将放弃未应用的表单输入。", () => Run(() =>
        {
            Session.Redo();
            AfterChange("已重做数据修改。");
        }));
    }

    public async Task SaveAsync()
    {
        if (!EnsureApplied())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var receipt = await store.SaveAsync(Project, Session.SavedETag);
            Session.MarkSaved(receipt);
            LastSavedPath = receipt.Path;
            LastBackupPath = receipt.BackupPath;
            Library = await store.ListAsync();
            Message("项目实际保存成功：" + receipt.Path);
        });
    }

    public void OpenStored(Guid id, bool backup = false)
    {
        AskAsync(backup ? "从备份打开" : "打开本机项目", "将替换当前工作副本。" +
            (HasUnsaved ? "有未保存内容，请先取消并保存。" : "确认继续？"), async () =>
        {
            await RunAsync(async () =>
            {
                var stored = backup ? await store.LoadBackupAsync(id) : await store.LoadAsync(id);
                Session.Open(stored.Project, stored.ETag, stored.Path, changed: backup);
                ResetWorkspace();
                Message(backup ? "备份已打开为未保存副本，确认内容后显式保存。" : "项目已从磁盘打开：" + stored.Path);
            });
        });
    }

    public void ImportProject(byte[] bytes, string filename)
    {
        Run(() =>
        {
            var decoded = new ProjectCodec().Decode(bytes);
            Ask("导入项目文件", "将以“" + decoded.Project.Name + "”替换工作副本。原文件不会被修改；保存前可复制为新项目标识。" +
                (HasUnsaved ? "当前有未保存内容，确认前请取消并先保存。" : ""), () =>
            {
                Session.Open(decoded.Project, path: filename, changed: true);
                ResetWorkspace();
                MigrationNotes = decoded.Notes;
                Message("已导入" + filename + (decoded.Migrated ? "并完成内存迁移，请另存新格式。" : "；本机已有同标识时须先打开磁盘版本或复制项目再保存。"));
            });
        });
    }

    public async Task RefreshLibraryAsync()
    {
        await RunAsync(async () => Library = await store.ListAsync());
    }

    public void LoadCsv(byte[] bytes, string filename)
    {
        Run(() =>
        {
            Import.Content = CsvImportService.DecodeUtf8(bytes);
            Import.Filename = filename;
            Import.Invalidate();
            Message("已读取CSV到预检区，尚未修改测量。");
        });
    }

    public void PreviewCsv()
    {
        Run(() =>
        {
            var preview = csv.Preview(Project, Import.Content, Import.Mode, Import.DelimiterCharacter());
            Import.SetPreview(preview);
            Message(preview.CanApply ? "CSV预检通过，请核对覆盖范围再确认导入。" : "CSV预检发现错误，整批不可应用。", !preview.CanApply);
        });
    }

    public void ApplyCsv()
    {
        if (Import.Preview is not { } preview)
        {
            Message("请先预检CSV。", true);
            return;
        }

        Ask("确认CSV整体导入", $"{preview.Rows}行、{preview.Changes.Count}个试验、覆盖{preview.Replaced}个同频带、缺失{preview.Missing}个。模式：{preview.Mode}。来源沿用试验档案。", () => Run(() =>
        {
            csv.Apply(Session, preview);
            AfterChange("CSV已整体应用，错误或过期预检不会改动数据。");
        }));
    }

    public void PreviewBatch()
    {
        Run(() =>
        {
            Batch.SetPreview(pairing.Preview(Project, Batch.BaselineId, Batch.TargetIds.ToList(), Batch.Purpose));
            Message("批量配对预检完成，请逐行核对。共享基线不作为独立配对估计区间。");
        });
    }

    public void ApplyBatch()
    {
        if (Batch.Preview is not { } preview)
        {
            Message("先选择基线和目标并预检。", true);
            return;
        }

        Ask("确认显式批量配对", "将建立" + preview.Rows.Count + "条明确对应关系；条件不符的配对保留阻断原因。", () => Run(() =>
        {
            pairing.Apply(Session, preview);
            Batch = Track(new BatchPairEditor());
            AfterChange("批量配对已建立，未按名称或时间自动推断。");
        }));
    }

    public void MarkReviewDirty()
    {
        ReviewDraftDirty = true;
        Notify();
    }

    public void RecordReview()
    {
        if (!EnsureApplied(allowReview: true))
        {
            return;
        }

        Run(() =>
        {
            Session.RecordReview(Reviewer, ReviewComment);
            ReviewDraftDirty = false;
            AfterChange("已记录输入修订、参数、哈希和阻断原因；这是用户复核记录，不是认证证书。");
        });
    }

    public void PrepareExport(string kind)
    {
        if (!EnsureApplied())
        {
            return;
        }

        Run(() =>
        {
            var document = kind switch
            {
                "project" => export.Project(Project),
                "measurements" => export.Measurements(Project),
                "results" => export.Results(Project, Analysis),
                "analysis" => export.AnalysisJson(Project, Analysis),
                "report" => export.Report(Project, Analysis),
                _ => throw new DomainValidationException("export", "导出类型无效")
            };
            DownloadUrl = downloads.Add(document);
            DownloadFilename = document.Filename;
            Message("文件已准备，点击下载链接。浏览器实际下载位置由其设置决定；准备导出不会替代本机保存。");
        });
    }

    public void NavigateIssue(QualityIssue issue)
    {
        AskDraft("定位问题前将放弃未应用表单。", () =>
        {
            ReloadEditors();
            switch (issue.EntityType)
            {
                case "condition":
                    Page = "conditions";
                    selectedCondition = issue.EntityId;
                    Condition = Track(new ConditionEditor(Project.Conditions.Single(item => item.Id == issue.EntityId)));
                    break;
                case "trial":
                    Page = "trials";
                    selectedTrial = issue.EntityId;
                    Trial = Track(new TrialEditor(Project.Trials.Single(item => item.Id == issue.EntityId)));
                    break;
                case "pair":
                    Page = "pairs";
                    selectedPair = issue.EntityId;
                    Pair = Track(new PairEditor(Project.Pairs.Single(item => item.Id == issue.EntityId)));
                    break;
                default:
                    Page = "project";
                    break;
            }

            Message(issue.Repair);
        });
    }

    public List<AcousticTrial> FilteredTrials()
    {
        return Project.Trials.Where(trial => (!TrialFilter.HasValue || trial.Kind == TrialFilter) &&
            (string.IsNullOrWhiteSpace(TrialSearch) || (trial.Name + trial.Id + trial.RepeatGroup)
                .Contains(TrialSearch.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public List<AcousticTrial> VisibleTrials()
    {
        return FilteredTrials().Skip(TrialPage * PageSize).Take(PageSize).ToList();
    }

    public void ApplyTrialFilter()
    {
        TrialPage = 0;
        Notify();
    }

    public void NextTrials(int change)
    {
        var pages = Math.Max(1, (FilteredTrials().Count + PageSize - 1) / PageSize);
        TrialPage = Math.Clamp(TrialPage + change, 0, pages - 1);
        Notify();
    }

    public List<QualityIssue> VisibleIssues()
    {
        return Analysis.Issues.Where(issue => QualitySeverity == "all" || issue.Severity.ToString() == QualitySeverity).ToList();
    }

    public void ApplyScan()
    {
        Run(Filter.ApplyRange);
    }

    public void Edit(Action action)
    {
        Run(action);
    }

    public void ClearNotice()
    {
        Notice = null;
        Problems = [];
        Notify();
    }

    public void CancelConfirmation()
    {
        confirmedAction = null;
        Confirmation = "";
        Notify();
    }

    public async Task ConfirmAsync()
    {
        var action = confirmedAction;
        confirmedAction = null;
        Confirmation = "";
        if (action is not null)
        {
            await action();
        }

        Notify();
    }

    private void AfterChange(string message, bool reload = true)
    {
        if (reload)
        {
            ReloadEditors();
        }

        Import.ClearPreview();
        Message(message + " 当前工作副本尚未保存。");
    }

    private void ResetWorkspace()
    {
        selectedLocation = null;
        selectedCondition = null;
        selectedTrial = null;
        selectedPair = null;
        Page = "project";
        TrialPage = 0;
        TrialSearch = "";
        TrialFilter = null;
        MigrationNotes = [];
        ReviewDraftDirty = false;
        Reviewer = "";
        ReviewComment = "";
        DownloadUrl = "";
        DownloadFilename = "";
        Import = Track(new ImportEditor());
        Batch = Track(new BatchPairEditor());
        Filter.Reset();
        Workbook.Clear();
        Scenario.Clear();
        ReloadEditors();
        Notify();
    }

    private void ReloadEditors()
    {
        Metadata = Track(new MetadataEditor(Project));
        Location = Track(new LocationEditor(Project.Locations.FirstOrDefault(item => item.Id == selectedLocation)));
        Condition = Track(new ConditionEditor(Project.Conditions.FirstOrDefault(item => item.Id == selectedCondition)));
        Trial = Track(new TrialEditor(Project.Trials.FirstOrDefault(item => item.Id == selectedTrial), Project.Conditions.FirstOrDefault()?.Id));
        Pair = Track(new PairEditor(Project.Pairs.FirstOrDefault(item => item.Id == selectedPair)));
        Settings = Track(new SettingsEditor(Project.Settings));
    }

    private EditorViewModel? ActiveEditor()
    {
        return Page switch
        {
            "project" => Metadata,
            "locations" => Location,
            "conditions" => Condition,
            "trials" => Trial,
            "pairs" => Pair,
            "quality" => Settings,
            "import" => Import,
            _ => null
        };
    }

    private T Track<T>(T editor) where T : ObservableObject
    {
        editor.Changed += () => Notify();
        return editor;
    }

    private bool EnsureApplied(bool allowReview = false)
    {
        if (ActiveEditor()?.IsDirty == true || (Page == "pairs" && Batch.IsDirty) || (ReviewDraftDirty && !allowReview))
        {
            Message("表单尚未应用。先应用修改或取消草稿，再保存、复核或导出。", true);
            return false;
        }

        return true;
    }

    private void AskDraft(string text, Action action)
    {
        if (HasDraft)
        {
            Ask("未应用表单", text, action);
        }
        else
        {
            action();
        }
    }

    private void AskReplace(string title, Action action)
    {
        if (HasUnsaved)
        {
            Ask(title, "当前工作副本有未保存内容。请先取消并保存；确认后将替换工作副本。", action);
        }
        else
        {
            action();
        }
    }

    private void Ask(string title, string message, Action action)
    {
        AskAsync(title, message, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private void AskAsync(string title, string message, Func<Task> action)
    {
        ConfirmationTitle = title;
        Confirmation = message;
        confirmedAction = action;
        Notify();
    }

    private void Message(string text, bool error = false)
    {
        Notice = new UserNotice(text, error);
        Notify();
    }

    private void Run(Action task)
    {
        if (Busy)
        {
            return;
        }

        Problems = [];
        try
        {
            task();
        }
        catch (DomainValidationException error)
        {
            Problems = error.Problems.ToList();
            Message("操作未应用。请按字段提示修正，原项目数据保留。", true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ProjectConflictException)
        {
            Message(error.Message, true);
        }

        Notify();
    }

    private async Task RunAsync(Func<Task> task)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        Problems = [];
        Notify();
        try
        {
            await task();
        }
        catch (DomainValidationException error)
        {
            Problems = error.Problems.ToList();
            Message("操作未完成，输入未覆盖。", true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ProjectConflictException)
        {
            Message(error.Message, true);
        }
        finally
        {
            Busy = false;
            Notify();
        }
    }
}
