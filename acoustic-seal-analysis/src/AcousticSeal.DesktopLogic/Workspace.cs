using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.Core.Operations;

namespace AcousticSeal.DesktopLogic;

public sealed class Workspace : Bindable
{
    private readonly IUserDialogs dialogs;
    private readonly DesktopFiles files = new();
    private readonly ProjectSession session = new();
    private readonly CsvImportService importer = new();
    private EntityEditor? editor;
    private NavigationItem? selectedModule;
    private ObjectItem? selectedItem;
    private PairResult? selectedResult;
    private string status = "空白项目。先填写项目，再建立位置、工况与试验。";
    private bool changing;
    private ImportPreview? importPreview;
    private string importText = "";
    private string importStatus = "尚未预检。固定表头：trial_id,frequency_hz,level_db；UTF-8，句点小数。";
    private ImportMode importMode;
    private string delimiter = ",";
    private string minimum = "";
    private string maximum = "";
    private double? appliedMinimum;
    private double? appliedMaximum;
    public ObservableCollection<NavigationItem> Modules { get; } =
    [
        new("project", "01  项目档案", "目标 / 来源 / 版本"),
        new("location", "02  封堵位置", "材料 / 覆盖 / 恢复工艺"),
        new("condition", "03  测量工况", "口径 / 单位 / 标定"),
        new("trial", "04  试验工作表", "基准 / 单项 / 组合 / 恢复"),
        new("import", "05  频带批量导入", "预检后整体应用"),
        new("pair", "06  显式配对", "选择基线与目标"),
        new("result", "07  差值与频谱", "对比 / 范围 / 原始核对"),
        new("quality", "08  质量检查", "重复性 / 漂移 / 恢复"),
        new("settings", "09  工程参数", "修改后重算全部历史输入"),
        new("audit", "10  审计与复核", "修订 / 快照 / 输入指纹")
    ];
    public ObservableCollection<ObjectItem> Items { get; } = [];
    public ObservableCollection<PairResult> Results { get; } = [];
    public ObservableCollection<BandDifference> ResultBands { get; } = [];
    public ObservableCollection<QualityRow> Quality { get; } = [];
    public ObservableCollection<AuditEntry> Audit { get; } = [];
    public ObservableCollection<ReviewRecord> Reviews { get; } = [];
    public EntityEditor? Editor { get => editor; private set { editor = value; Notify(); Notify(nameof(HasEditor)); } }
    public bool HasEditor => Editor is not null;
    public NavigationItem? SelectedModule
    {
        get => selectedModule;
        set
        {
            if (changing || value == selectedModule) return;
            if (!ResolveDraft()) { Notify(); return; }
            selectedModule = value;
            selectedItem = null;
            Notify();
            Refresh();
        }
    }
    public ObjectItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (changing || value == selectedItem) return;
            if (!ResolveDraft()) { Notify(); return; }
            selectedItem = value;
            Notify();
            LoadEditor();
        }
    }
    public PairResult? SelectedResult
    {
        get => selectedResult;
        set { selectedResult = value; Notify(); LoadResult(); }
    }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Title => Software.Name + " V3.0 · " + session.Project.Name + (HasChanges ? " *" : "");
    public string ProjectSummary => $"修订 {session.Project.Revision}  |  {session.Project.Locations.Count} 位置  ·  {session.Project.Trials.Count} 试验  ·  {session.Project.Pairs.Count} 配对";
    public string SourceSummary => session.Project.SourceSummary();
    public string FilePath => files.Path.Length == 0 ? "尚未保存到磁盘" : files.Path;
    public string PageTitle => SelectedModule?.Title ?? "项目档案";
    public string PageHint => SelectedModule?.Hint ?? "";
    public string Inspector => $"可比较 {session.Analysis.ValidPairCount} / 阻断 {session.Analysis.BlockedPairCount} / 警示 {session.Analysis.WarningCount}\n\n" + Software.Limitation + "\n\n" + Software.ScreeningNote;
    public bool HasChanges => session.Dirty || Editor?.Dirty == true;
    public bool IsList => SelectedModule?.Key is "location" or "condition" or "trial" or "pair";
    public bool IsResult => SelectedModule?.Key == "result";
    public bool IsQuality => SelectedModule?.Key == "quality";
    public bool IsAudit => SelectedModule?.Key == "audit";
    public bool IsImport => SelectedModule?.Key == "import";
    public bool IsTrial => SelectedModule?.Key == "trial";
    public bool IsPair => SelectedModule?.Key == "pair";
    public string ImportText { get => importText; set { if (Set(ref importText, value)) InvalidateImport(); } }
    public ImportMode ImportMode { get => importMode; set { if (Set(ref importMode, value)) InvalidateImport(); } }
    public string Delimiter { get => delimiter; set { if (Set(ref delimiter, value)) InvalidateImport(); } }
    public string ImportStatus { get => importStatus; private set => Set(ref importStatus, value); }
    public string Minimum { get => minimum; set => Set(ref minimum, value); }
    public string Maximum { get => maximum; set => Set(ref maximum, value); }
    public string ResultNote => selectedResult is null ? "尚无配对。先在显式配对建立记录。" :
        selectedResult.Status.Text() + " · " + selectedResult.Purpose.Text() + " · " + selectedResult.Source.Text() + "\n" +
        string.Join("；", selectedResult.BlockingReasons.Concat(selectedResult.Warnings)) + "\n" + selectedResult.TotalNote +
        (selectedResult.Screening is { } screen ? $"\n最大绝对差 {Software.Format(screen.MaximumAbsoluteDifference)} dB / 阈值 {screen.Threshold} dB / {screen.Status.Text()}" : "");
    public string Reviewer { get; set; } = "";
    public string ReviewComment { get; set; } = "";
    public string PasteText { get; set; } = "";
    public BandDraft? SelectedBand { get; set; }
    public List<Choice> ScenarioParameters => session.Project.Settings.Describe().Select(item => new Choice(item.Key, item.Label)).ToList();
    public string ScenarioKey { get; set; } = "RecoveryThreshold";
    public string ScenarioValues { get; set; } = "0.5,1,2";
    public string AuxiliaryResult { get; private set; } = "规则情景比较同一输入的不同判读，不更改测量；输出是辅助核对文件。";
    public ActionCommand NewCommand { get; }
    public ActionCommand DemoCommand { get; }
    public ActionCommand OpenCommand { get; }
    public ActionCommand SaveCommand { get; }
    public ActionCommand SaveAsCommand { get; }
    public ActionCommand ApplyCommand { get; }
    public ActionCommand RevertCommand { get; }
    public ActionCommand AddCommand { get; }
    public ActionCommand DeleteCommand { get; }
    public ActionCommand CopyCommand { get; }
    public ActionCommand DuplicateTrialCommand { get; }
    public ActionCommand UndoCommand { get; }
    public ActionCommand RedoCommand { get; }
    public ActionCommand AddBandCommand { get; }
    public ActionCommand RemoveBandCommand { get; }
    public ActionCommand PasteCommand { get; }
    public ActionCommand ImportFileCommand { get; }
    public ActionCommand PreviewCommand { get; }
    public ActionCommand ImportCommand { get; }
    public ActionCommand ReportCommand { get; }
    public ActionCommand ResultsCommand { get; }
    public ActionCommand MeasurementsCommand { get; }
    public ActionCommand JsonCommand { get; }
    public ActionCommand ReviewCommand { get; }
    public ActionCommand FilterCommand { get; }
    public ActionCommand WorkbookCommand { get; }
    public ActionCommand RevisionCommand { get; }
    public ActionCommand ScenarioCommand { get; }
    public ActionCommand ArchiveCommand { get; }

    public Workspace(IUserDialogs dialogs)
    {
        this.dialogs = dialogs;
        ActionCommand Command(Action action) => new(() => { action(); return Task.CompletedTask; }, Fail);
        NewCommand = Command(() => Replace(AcousticProject.Empty(), "已新建空白项目。"));
        DemoCommand = Command(() => Replace(Demonstration.Create(), "已载入独立模拟案例；全部数据为模拟，不代表实车效果。"));
        OpenCommand = Command(Open);
        SaveCommand = Command(() => Save(false));
        SaveAsCommand = Command(() => Save(true));
        ApplyCommand = Command(Apply);
        RevertCommand = Command(() => { if (Editor?.Dirty != true || dialogs.Confirm("放弃编辑", "放弃当前未应用编辑？")) LoadEditor(); });
        AddCommand = Command(Add);
        DeleteCommand = Command(Delete);
        CopyCommand = Command(CopyProject);
        DuplicateTrialCommand = Command(DuplicateTrial);
        UndoCommand = Command(() => { if (!ResolveDraft()) return; session.Undo(); Refresh(); Status = "已撤销输入；累计审计保留。"; });
        RedoCommand = Command(() => { if (!ResolveDraft()) return; session.Redo(); Refresh(); Status = "已重做输入。"; });
        AddBandCommand = Command(() => Editor?.AddBand());
        RemoveBandCommand = Command(() => Editor?.RemoveBand(SelectedBand));
        PasteCommand = Command(() => { Editor?.Paste(PasteText); Status = "两列数据已放入草稿，点击应用编辑保存到项目。"; });
        ImportFileCommand = Command(ReadCsv);
        PreviewCommand = Command(Preview);
        ImportCommand = Command(Import);
        ReportCommand = Command(() => Export(new ResultExport().Report(session.Project, session.Analysis), "HTML报告|*.html"));
        ResultsCommand = Command(() => Export(new ResultExport().Results(session.Project, session.Analysis), "结果CSV|*.csv"));
        MeasurementsCommand = Command(() => Export(new ResultExport().Measurements(session.Project), "频带CSV|*.csv"));
        JsonCommand = Command(() => Export(new ResultExport().AnalysisJson(session.Project, session.Analysis), "分析JSON|*.json"));
        ReviewCommand = Command(() => { if (!ResolveDraft()) return; session.RecordReview(Reviewer, ReviewComment); Refresh(); Status = "已记录输入指纹及当前分析快照，请保存项目。"; });
        FilterCommand = Command(Filter);
        WorkbookCommand = Command(Workbook);
        RevisionCommand = Command(CompareRevision);
        ScenarioCommand = Command(Scenario);
        ArchiveCommand = Command(() => { if (!ResolveDraft()) return; ProjectCommands.SetArchived(session, session.Project.Status != ProjectStatus.Archived); Refresh(); Status = "项目状态：" + session.Project.Status + "；归档后可查看和导出。"; });
        selectedModule = Modules[0];
        Refresh();
    }

    private void Fail(Exception error)
    {
        Status = "操作未完成：" + error.Message;
        dialogs.ShowError(Status);
        Notify(nameof(Title));
    }
    private bool ResolveDraft()
    {
        if (Editor?.Dirty != true) return true;
        var answer = dialogs.SaveChanges("当前有未应用编辑。选择“是”应用编辑，“否”放弃编辑，“取消”保留当前工作页。");
        if (answer is null) return false;
        if (answer is true)
        {
            try { Apply(); }
            catch (Exception error) { Fail(error); return false; }
        }
        else LoadEditor();
        return true;
    }
    public bool CanClose()
    {
        try
        {
            if (!ResolveDraft()) return false;
            if (!session.Dirty) return true;
            var answer = dialogs.SaveChanges("项目有未保存修改。是否保存到磁盘后继续？");
            if (answer is null) return false;
            return answer is false || Save(false);
        }
        catch (Exception error) { Fail(error); return false; }
    }
    private void Replace(AcousticProject project, string message)
    {
        if (!CanClose()) return;
        session.Open(project, changed: project.Trials.Count > 0);
        files.Reset();
        selectedModule = Modules[0];
        selectedItem = null;
        Refresh();
        Status = message;
    }
    private void Open()
    {
        if (!CanClose()) return;
        var path = dialogs.OpenFile("打开项目或备份", "封堵项目/备份|*.sealproj;*.bak;*.aslp.json|所有文件|*.*");
        if (path is null) return;
        var project = files.Open(path);
        session.Open(project, changed: files.Path.Length == 0);
        selectedModule = Modules[0];
        selectedItem = null;
        Refresh();
        Status = "已打开：" + path + files.OpenNotes;
    }
    private bool Save(bool saveAs)
    {
        if (!ResolveDraft()) return false;
        var target = !saveAs && files.Path.Length > 0 ? files.Path : dialogs.SaveFile("保存项目", "封堵项目|*.sealproj", ResultExport.SafeName(session.Project.Name) + Software.Extension);
        if (target is null) return false;
        var different = !string.Equals(target, files.Path, StringComparison.OrdinalIgnoreCase);
        var overwrite = different && File.Exists(target);
        if (overwrite && !dialogs.Confirm("覆盖项目", "目标存在，将保留.bak备份并覆盖：" + target)) return false;
        var receipt = files.Save(session.Project, target, overwrite);
        session.MarkSaved(receipt);
        NotifyState();
        Status = "已保存：" + receipt.Path + (receipt.BackupPath.Length > 0 ? "；备份：" + receipt.BackupPath : "");
        return true;
    }
    public void Apply()
    {
        if (Editor is null) return;
        var model = Editor.Read();
        switch (model)
        {
            case AcousticProject metadata:
                session.Apply("应用项目档案", metadata.Id.ToString(), project =>
                {
                    project.Name = metadata.Name; project.Owner = metadata.Owner;
                    project.VehicleProgramme = metadata.VehicleProgramme; project.Objective = metadata.Objective;
                    project.Description = metadata.Description;
                });
                break;
            case SealLocation location: ProjectCommands.SaveLocation(session, location); break;
            case MeasurementCondition condition: ProjectCommands.SaveCondition(session, condition); break;
            case AcousticTrial trial: ProjectCommands.SaveTrial(session, trial); break;
            case TrialPair pair: ProjectCommands.SavePair(session, pair); break;
            case AnalysisSettings settings: ProjectCommands.SaveSettings(session, settings); break;
        }
        var id = Guid.TryParse(Editor.Identity, out var key) ? key : Guid.Empty;
        Refresh(id);
        Status = "已应用编辑并重新计算。项目尚需保存到磁盘。";
    }
    private void Add()
    {
        if (!ResolveDraft()) return;
        object model = SelectedModule?.Key switch
        {
            "location" => new SealLocation(),
            "condition" => new MeasurementCondition(),
            "trial" => new AcousticTrial { ConditionId = session.Project.Conditions.FirstOrDefault()?.Id ?? Guid.Empty },
            "pair" => new TrialPair(),
            _ => throw new DomainValidationException("工作页", "仅位置、工况、试验、配对可新增")
        };
        selectedItem = null;
        Notify(nameof(SelectedItem));
        SetEditor(model);
        Status = "新增草稿，必填项完成后点击应用编辑；来源说明不可空白。";
    }
    private void Delete()
    {
        if (SelectedItem is null) throw new DomainValidationException("删除", "先选择列表中的对象");
        if (!dialogs.Confirm("删除对象", "删除“" + SelectedItem.Title + "”？关联引用存在时会阻止删除。")) return;
        var id = SelectedItem.Id;
        switch (SelectedModule?.Key)
        {
            case "location": ProjectCommands.DeleteLocation(session, id); break;
            case "condition": ProjectCommands.DeleteCondition(session, id); break;
            case "trial": ProjectCommands.DeleteTrial(session, id); break;
            case "pair": ProjectCommands.DeletePair(session, id); break;
        }
        selectedItem = null;
        Refresh();
        Status = "已删除；可通过撤销恢复，保存前磁盘文件未改变。";
    }
    private void CopyProject()
    {
        if (!ResolveDraft()) return;
        session.MakeCopy(); files.Reset(); selectedItem = null; Refresh();
        Status = "已创建新的项目标识，来源仍保留原记录；请另存。";
    }
    private void DuplicateTrial()
    {
        if (!ResolveDraft()) return;
        if (SelectedModule?.Key == "trial" && SelectedItem is not null)
        {
            var id = ProjectCommands.DuplicateTrial(session, SelectedItem.Id);
            Refresh(id);
            Status = "已复制试验结构；未复制频带测量和配对，请核对时间和来源。";
        }
        else throw new DomainValidationException("复制试验结构", "先在试验工作表选择一条已应用试验");
    }
    private void Refresh(Guid? focus = null)
    {
        changing = true;
        var id = focus ?? selectedItem?.Id;
        Items.Clear();
        IEnumerable<ObjectItem> values = SelectedModule?.Key switch
        {
            "location" => session.Project.Locations.Select(item => new ObjectItem(item.Id, item.Name, item.Zone + " · " + item.Material)),
            "condition" => session.Project.Conditions.Select(item => new ObjectItem(item.Id, item.Name, item.Vehicle + " · " + item.Weighting)),
            "trial" => session.Project.Trials.Select(item => new ObjectItem(item.Id, item.Name, item.Kind.Text() + " · " + item.Source.Text())),
            "pair" => session.Project.Pairs.Select(item => new ObjectItem(item.Id, session.Project.TrialName(item.BaselineId) + " → " + session.Project.TrialName(item.TargetId), item.Purpose.Text())),
            _ => []
        };
        foreach (var value in values) Items.Add(value);
        selectedItem = Items.FirstOrDefault(item => item.Id == id) ?? Items.FirstOrDefault();
        var resultId = selectedResult?.PairId;
        Results.Clear();
        foreach (var result in session.Analysis.Pairs) Results.Add(result);
        selectedResult = Results.FirstOrDefault(item => item.PairId == resultId) ?? Results.FirstOrDefault();
        Quality.Clear();
        foreach (var issue in session.Analysis.Issues) Quality.Add(new(issue.Severity.Text(), issue.Title, issue.Reason, issue.Repair));
        foreach (var repeat in session.Analysis.Repeatability)
            foreach (var band in repeat.Bands)
                Quality.Add(new(band.Status.Text(), repeat.Name + " / " + band.Frequency + " Hz", $"n={band.Statistics.Count}, SD={Software.Format(band.Statistics.StandardDeviation)} dB, 缺失={band.MissingCount}；" + string.Join("；", repeat.BlockingReasons), "同组工况须两两可比，n<2不产生样本标准差。"));
        Audit.Clear(); foreach (var entry in session.Project.Audit.AsEnumerable().Reverse()) Audit.Add(entry);
        Reviews.Clear(); foreach (var review in session.Project.Reviews.AsEnumerable().Reverse()) Reviews.Add(review);
        InvalidateImport();
        changing = false;
        LoadEditor();
        LoadResult();
        NotifyState();
    }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(Title), nameof(ProjectSummary), nameof(SourceSummary), nameof(FilePath), nameof(PageTitle), nameof(PageHint), nameof(Inspector), nameof(SelectedModule), nameof(SelectedItem), nameof(SelectedResult), nameof(IsList), nameof(IsResult), nameof(IsQuality), nameof(IsAudit), nameof(IsImport), nameof(IsTrial), nameof(IsPair), nameof(ScenarioParameters) }) Notify(property);
    }
    private void LoadEditor()
    {
        object? model = SelectedModule?.Key switch
        {
            "project" => ProjectCodec.Clone(session.Project),
            "settings" => ProjectCodec.Clone(session.Project.Settings),
            "location" => session.Project.Locations.FirstOrDefault(item => item.Id == selectedItem?.Id) is { } location ? ProjectCodec.Clone(location) : null,
            "condition" => session.Project.Conditions.FirstOrDefault(item => item.Id == selectedItem?.Id) is { } condition ? ProjectCodec.Clone(condition) : null,
            "trial" => session.Project.Trials.FirstOrDefault(item => item.Id == selectedItem?.Id) is { } trial ? ProjectCodec.Clone(trial) : null,
            "pair" => session.Project.Pairs.FirstOrDefault(item => item.Id == selectedItem?.Id) is { } pair ? ProjectCodec.Clone(pair) : null,
            _ => null
        };
        SetEditor(model);
    }
    private void SetEditor(object? model)
    {
        Editor = model is null ? null : new EntityEditor(model, session.Project);
        if (Editor is not null) Editor.PropertyChanged += (_, _) => Notify(nameof(Title));
    }
    private void ReadCsv()
    {
        var path = dialogs.OpenFile("导入UTF-8频带CSV", "CSV|*.csv|所有文件|*.*");
        if (path is null) return;
        var info = new FileInfo(path);
        if (info.Length > 5 * 1024 * 1024) throw new DomainValidationException("CSV", "文件超过5MiB");
        ImportText = CsvImportService.DecodeUtf8(File.ReadAllBytes(path));
        Status = "已读取CSV，请预检并核对覆盖数量：" + path;
    }
    private void InvalidateImport()
    {
        importPreview = null;
        ImportStatus = "当前内容尚未预检。修改内容、分隔符、模式或项目后须重新预检。";
    }
    private void Preview()
    {
        importPreview = importer.Preview(session.Project, ImportText, ImportMode, Delimiter == "tab" ? '\t' : Delimiter[0]);
        ImportStatus = $"数据 {importPreview.Rows} 行；覆盖 {importPreview.Replaced}；缺失 {importPreview.Missing}；" +
            (importPreview.CanApply ? "可以应用。" : "预检失败，整批未应用。") + "\n" +
            string.Join("\n", importPreview.Changes.Select(item => item.TrialName + $"：新增 {item.Added}，覆盖 {item.Replaced}，缺失 {item.Missing}")) + "\n" +
            string.Join("\n", importPreview.Problems.Select(item => item.ToString()));
    }
    private void Import()
    {
        if (importPreview is null) throw new DomainValidationException("导入", "先预检当前内容");
        if (!importPreview.CanApply) throw new DomainValidationException("导入", "预检未通过，请修复指定行");
        if (!dialogs.Confirm("整体应用导入", ImportStatus + "\n确认应用？替换模式将清空涉及试验的旧频带。")) return;
        importer.Apply(session, importPreview);
        Refresh(); Status = "已整体应用导入并重新计算，请保存项目。";
    }
    private void Export(ExportDocument document, string filter)
    {
        if (Editor?.Dirty == true) throw new DomainValidationException("导出", "先应用或放弃当前编辑，避免导出旧输入");
        var path = dialogs.SaveFile("导出成果", filter, document.Filename);
        if (path is null) return;
        DesktopFiles.Export(path, document.Bytes);
        Status = "已导出：" + path;
    }
    private void Filter()
    {
        double? Parse(string text) => string.IsNullOrWhiteSpace(text) ? null :
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value is >= 1 and <= 100000 ? value : throw new DomainValidationException("频带范围", "请输入1—100000Hz或空白");
        var low = Parse(Minimum); var high = Parse(Maximum);
        if (low > high) throw new DomainValidationException("频带范围", "下限不得大于上限");
        appliedMinimum = low; appliedMaximum = high; LoadResult();
        Status = "已筛选显示频带，完整报告仍导出全部频带。";
    }
    private void LoadResult()
    {
        ResultBands.Clear();
        foreach (var band in selectedResult?.Bands ?? [])
            if ((!appliedMinimum.HasValue || band.Frequency >= appliedMinimum) && (!appliedMaximum.HasValue || band.Frequency <= appliedMaximum)) ResultBands.Add(band);
        Notify(nameof(ResultNote));
        Notify(nameof(SelectedResult));
    }
    private void Workbook()
    {
        if (selectedResult is null) throw new DomainValidationException("原始核对", "请先建立并选择一个显式配对");
        var ids = new List<Guid> { selectedResult.BaselineId, selectedResult.TargetId };
        var workbook = new InputWorkbook().Build(session.Project, ids, appliedMinimum, appliedMaximum);
        Export(new ExportDocument("原始频带核对.csv", "text/csv", new InputWorkbook().Csv(workbook)), "CSV核对表|*.csv");
    }
    private void CompareRevision()
    {
        var path = dialogs.OpenFile("选择用于输入内容核对的项目（当前工作项目保持原样）", "项目|*.sealproj;*.bak|所有文件|*.*");
        if (path is null) return;
        var right = new ProjectCodec().Decode(DesktopFiles.Read(path)).Project;
        var result = new RevisionComparison().Compare(session.Project, right);
        AuxiliaryResult = JsonSerializer.Serialize(result, ProjectCodec.Options);
        Notify(nameof(AuxiliaryResult));
        Export(new ExportDocument("项目输入版本核对.json", "application/json", Encoding.UTF8.GetBytes(AuxiliaryResult)), "JSON核对|*.json");
    }
    private void Scenario()
    {
        var values = ScenarioValues.Split(',').Select(text => double.Parse(text.Trim(), CultureInfo.InvariantCulture)).ToList();
        var service = new SettingsScenarios();
        var result = service.Compare(session.Project, ScenarioKey, values);
        AuxiliaryResult = result.Note + "\n" + string.Join("\n", result.Scenarios.Select(item => $"{item.Value}：可比{item.ValidPairs} / 阻断{item.BlockedPairs} / 漂移恢复警示{item.ScreeningAlerts} / 重复性警示{item.RepeatabilityAlerts}"));
        Notify(nameof(AuxiliaryResult));
        Export(service.Export(result), "规则情景JSON|*.json");
    }
}

public sealed record QualityRow(string Status, string Title, string Reason, string Repair);
