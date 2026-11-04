using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Blower.Core.Analysis;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Core.Operations;
using Blower.Desktop.Services;

namespace Blower.Desktop.ViewModels;

public sealed record WorkspaceNode(string Key, string Title, string Hint);
public sealed record PropertyRow(string 项目, string 内容);

public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly IDesktopDialogs dialogs;
    private readonly ProjectSession session = new();
    private readonly List<RelayCommand> commands = [];
    private Project draft = new();
    private string savedContent = "";
    private string diskToken = "";
    private string filePath = "";
    private string notice = "空白项目：先填写项目来源和规则，再添加阶次、模态与挡位方案。";
    private bool error;
    private bool busy;
    private WorkspaceNode? selectedNode;
    private object? selectedRow;
    private SpeedPlan? activePlan;
    private IEnumerable rows = Array.Empty<object>();
    private string contactSearch = "";
    private bool onlyNear = true;
    private SpeedPlan? comparePlan;
    private double? chartCeiling;
    private string section = "project";

    public WorkspaceViewModel(IDesktopDialogs dialogs)
    {
        this.dialogs = dialogs;
        NewCommand = Command(New);
        OpenCommand = Command(Open);
        SaveCommand = Command(() => Save(false));
        SaveAsCommand = Command(() => Save(true));
        DemoCommand = Command(LoadDemo);
        DuplicateProjectCommand = Command(DuplicateProject);
        UndoCommand = Command(() => History(false), () => session.CanUndo && !busy);
        RedoCommand = Command(() => History(true), () => session.CanRedo && !busy);
        ApplyCommand = Command(ApplyEditor, () => !Project.Archived && !busy);
        ResetEditorCommand = Command(ResetEditor);
        AddCommand = Command(Add, () => !Project.Archived && !busy);
        DeleteCommand = Command(Delete, () => !Project.Archived && !busy);
        PlanPropertiesCommand = Command(() => SelectedRow = null);
        AddPlanCommand = Command(() => AddPlan(), () => !Project.Archived && !busy);
        AddGearCommand = Command(() => AddGear(), () => ActivePlan != null && !Project.Archived && !busy);
        LibrarySaveCommand = Command(SaveLibrary);
        LibraryOpenCommand = Command(() => OpenLibrary(false));
        LibraryBackupCommand = Command(() => OpenLibrary(true));
        CopyPlanCommand = Command(CopyPlan, () => ActivePlan != null && !Project.Archived && !busy);
        ArchiveCommand = Command(ToggleArchive);
        ComputeCommand = Command(() => _ = CalculateAsync(), () => !busy);
        ScanCommand = Command(() => _ = ScanAsync(), () => !busy && ActivePlan != null);
        AcceptScanCommand = Command(AcceptScan, () => !busy && !Project.Archived && Scan?.Feasible == true);
        CompareCommand = Command(Compare);
        StudyCommand = Command(() => _ = StudyAsync(), () => !busy && ActivePlan != null);
        GenerateChecksCommand = Command(GenerateChecks, () => !busy && !Project.Archived);
        ImportReadCommand = Command(ReadCsv);
        PreviewCommand = Command(PreviewCsv);
        ImportApplyCommand = Command(ApplyCsv, () => !busy && !Project.Archived);
        TemplateCommand = Command(ExportTemplate);
        ExportContactsCommand = Command(() => ExportResult("csv", Exports.Contacts));
        ExportCrossingsCommand = Command(() => ExportResult("csv", Exports.Crossings));
        ExportReportCommand = Command(ExportReport);
        ExportSvgCommand = Command(ExportSvg);
        ExportChecksCommand = Command(() => { if (LeaveEditor()) Export("csv", Exports.Checks(Project)); });
        ExportStudyCommand = Command(ExportStudy);
        ExportComparisonCommand = Command(ExportComparison);
        Form.PropertyChanged += (_, _) => Raise(nameof(Title));
        Nodes = new ObservableCollection<WorkspaceNode>
        {
            new("library", "本地项目库", "00 保存项目及恢复备份"),
            new("project", "项目与来源", "01 设计对象与数据性质"),
            new("settings", "电机与工程规则", "02 rpm、叶片、半宽与扫描约束"),
            new("orders", "激励阶次", "03 轴阶次 / 叶片通过倍频"),
            new("modes", "模态库", "04 结构频率、阻尼及来源"),
            new("plans", "挡位方案", "05 独立方案及转速表"),
            new("plot", "转速—频率图", "06 阶次线、模态区间与挡位"),
            new("crossings", "交叉区间", "07 精确交叉及敏感转速范围"),
            new("contacts", "接近明细", "08 挡位 / 阶次 / 模态组合"),
            new("scan", "约束扫描", "09 固定挡位及最小间距"),
            new("compare", "方案比较", "10 目标减基准，按挡位序号"),
            new("study", "规则敏感性", "11 在副本上试算规则"),
            new("checks", "验证清单", "12 证据和复核状态"),
            new("review", "质量复核", "13 来源、阻尼及重叠提示"),
            new("exchange", "CSV 预检", "14 先预检，再整批应用"),
            new("audit", "修订记录", "15 可追溯的会话修改"),
            new("help", "使用与计算口径", "16 公式、范围与完整示例")
        };
        session.Load(new Project());
        selectedNode = Nodes.Single(x => x.Key == "project");
        Rebuild();
    }

    public ObservableCollection<WorkspaceNode> Nodes { get; }
    public EditorForm Form { get; } = new();
    public Project Project => session.Current;
    public IReadOnlyList<SpeedPlan> Plans => Project.Plans;
    public AnalysisResult? Result { get; private set; }
    public ScanResult? Scan { get; private set; }
    public RuleStudyResult? Study { get; private set; }
    public PlanComparison? Comparison { get; private set; }
    public ChartModel? Chart { get; private set; }
    public string Boundary => Software.Boundary;
    public string SoftwareTitle => Software.Name + " " + Software.Version;
    public string Title => SoftwareTitle + " — " + Project.Name + (Dirty || Form.Changed ? " *" : "");
    public string FilePath => string.IsNullOrEmpty(filePath) ? "尚未保存到文件" : filePath;
    public bool Dirty => ProjectCodec.Encode(Project) != savedContent;
    public string SourceLabel => Labels.Source(Project.Origin) + (Project.Archived ? " · 已归档" : " · 编辑中");
    public string RevisionLabel => $"修订 {Project.Revision} · {Project.Plans.Count} 方案 · {Project.Modes.Count} 模态";
    public string SectionTitle => SelectedNode?.Title ?? "";
    public string SectionHint => SelectedNode?.Hint ?? "";
    public bool IsTable => section != "plot" && section != "help";
    public bool IsPlot => section == "plot";
    public bool IsHelp => section == "help";
    public bool IsLibrary => section == "library";
    public bool IsPlanEditor => section == "plans";
    public bool IsPlans => section is "plans" or "scan" or "compare" or "study" or "plot" or "contacts";
    public bool IsContacts => section == "contacts";
    public bool IsScan => section == "scan";
    public bool IsCompare => section == "compare";
    public bool IsStudy => section == "study";
    public bool IsChecks => section == "checks";
    public bool IsExchange => section == "exchange";
    public bool HasEditor => Form.Fields.Count > 0;
    public bool Busy => busy;
    public bool Error => error;
    public string Notice => notice;
    public string ResultStatus => Result == null ? "尚未计算" :
        Result.InputHash != ProjectCodec.InputHash(Project) ? "结果已过期，请重新计算" :
        $"计算有效 · {Result.Time.LocalDateTime:HH:mm:ss} · {Result.Contacts.Count} 组合";
    public string ScanStatus => Scan?.Message ?? "尚未扫描。先选择包含挡位的方案。";
    public string ComparisonStatus => Comparison == null ? "基准为当前方案，选择另一目标方案后比较。" :
        $"{Comparison.Baseline} → {Comparison.Target}：接近组合 {Comparison.BaselineContacts} → {Comparison.TargetContacts}；接近挡位 {Comparison.BaselineNear} → {Comparison.TargetNear}。差异不代表声音改善。";
    public IEnumerable Rows
    {
        get => rows;
        private set => Set(ref rows, value);
    }
    public WorkspaceNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            if (value == selectedNode || value == null)
            {
                return;
            }
            if (!LeaveEditor())
            {
                Raise();
                return;
            }
            selectedNode = value;
            section = value.Key;
            selectedRow = null;
            Rebuild();
        }
    }
    public object? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (ReferenceEquals(value, selectedRow))
            {
                return;
            }
            if (!LeaveEditor())
            {
                Raise();
                return;
            }
            selectedRow = value;
            BuildEditor();
            Raise();
        }
    }
    public SpeedPlan? ActivePlan
    {
        get => activePlan;
        set
        {
            if (value?.Id == activePlan?.Id)
            {
                return;
            }
            if (!LeaveEditor())
            {
                Raise();
                return;
            }
            activePlan = value;
            selectedRow = null;
            RefreshRows();
            BuildEditor();
            RefreshChart();
            RefreshCommands();
            Raise();
        }
    }
    public SpeedPlan? ComparePlan
    {
        get => comparePlan;
        set => Set(ref comparePlan, value);
    }
    public string ContactSearch
    {
        get => contactSearch;
        set
        {
            if (Set(ref contactSearch, value))
            {
                RefreshRows();
            }
        }
    }
    public bool OnlyNear
    {
        get => onlyNear;
        set
        {
            if (Set(ref onlyNear, value))
            {
                RefreshRows();
            }
        }
    }
    public double? ChartCeiling
    {
        get => chartCeiling;
        set
        {
            if (value is <= 0 || value is > 20000000 || value.HasValue && !double.IsFinite(value.Value))
            {
                Message("图表上限须为 0～20000000Hz 范围内的正值；留空自动适配", true);
                Raise();
                return;
            }
            if (Set(ref chartCeiling, value))
            {
                RefreshChart();
            }
        }
    }

    private RelayCommand Command(Action action, Func<bool>? available = null)
    {
        var command = new RelayCommand(() => Attempt(action), available);
        commands.Add(command);
        return command;
    }
    private void Attempt(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Message(e.Message, true);
        }
    }
    private void Message(string text, bool failed = false)
    {
        notice = text;
        error = failed;
        Raise(nameof(Notice));
        Raise(nameof(Error));
        Raise(nameof(Title));
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        foreach (var command in commands)
        {
            command.Refresh();
        }
    }
    private void Rebuild()
    {
        var id = activePlan?.Id;
        activePlan = Project.Plans.FirstOrDefault(x => x.Id == id) ?? Project.Plans.FirstOrDefault();
        comparePlan = Project.Plans.FirstOrDefault(x => x.Id == comparePlan?.Id);
        RefreshRows();
        BuildEditor();
        RefreshChart();
        foreach (var property in new[]
        {
            nameof(Project), nameof(Plans), nameof(ActivePlan), nameof(ComparePlan), nameof(SelectedNode),
            nameof(SelectedRow), nameof(SectionTitle), nameof(SectionHint), nameof(IsTable), nameof(IsPlot),
            nameof(IsHelp), nameof(IsPlans), nameof(IsContacts), nameof(IsScan), nameof(IsCompare),
            nameof(IsStudy), nameof(IsChecks), nameof(IsLibrary), nameof(IsPlanEditor), nameof(IsExchange), nameof(Title), nameof(Dirty),
            nameof(SourceLabel), nameof(RevisionLabel), nameof(FilePath), nameof(ResultStatus),
            nameof(ScanStatus), nameof(ComparisonStatus)
        })
        {
            Raise(property);
        }
        RefreshCommands();
    }
    private void RefreshRows()
    {
        Rows = section switch
        {
            "library" => Library.List(),
            "project" => new[]
            {
                new PropertyRow("名称", Project.Name),
                new PropertyRow("负责人", Project.Owner),
                new PropertyRow("车辆 / 平台", Project.Vehicle),
                new PropertyRow("来源性质", Labels.Source(Project.Origin)),
                new PropertyRow("来源说明", Project.Source),
                new PropertyRow("项目标识", Project.Id),
                new PropertyRow("文件", FilePath)
            },
            "settings" => new[]
            {
                new PropertyRow("叶片数", Project.Settings.Blades.ToString()),
                new PropertyRow("转速范围 rpm", $"{Project.Settings.MinRpm} ～ {Project.Settings.MaxRpm}"),
                new PropertyRow("半宽口径", "max(绝对半宽, 相对半宽, 阻尼近似, 附加半宽)"),
                new PropertyRow("计算边界", Software.Boundary)
            },
            "orders" => Project.Orders,
            "modes" => Project.Modes,
            "plans" => (IEnumerable?)ActivePlan?.Gears ?? Array.Empty<Gear>(),
            "contacts" => (Result?.Contacts ?? [])
                .Where(x => x.PlanId == ActivePlan?.Id)
                .Where(x => !OnlyNear || x.Near)
                .Where(x => (x.Gear + x.Order + x.Mode).Contains(ContactSearch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.ClearanceHz).ToList(),
            "crossings" => (IEnumerable?)Result?.Crossings ?? Array.Empty<Crossing>(),
            "scan" => (IEnumerable?)Scan?.Choices ?? Array.Empty<ScanChoice>(),
            "compare" => (IEnumerable?)Comparison?.Rows ?? Array.Empty<GearDifference>(),
            "study" => (IEnumerable?)Study?.Cases ?? Array.Empty<RuleCase>(),
            "checks" => Project.Checks,
            "review" => QualityReview.Inspect(Project),
            "audit" => Project.Audit.AsEnumerable().Reverse().ToList(),
            "exchange" => (IEnumerable?)Preview?.Problems ?? Array.Empty<CsvProblem>(),
            _ => Array.Empty<object>()
        };
    }
    private void RefreshChart()
    {
        Chart = Result != null && Result.InputHash == ProjectCodec.InputHash(Project)
            ? ChartModel.Create(Project, Result, ActivePlan?.Id ?? "", ChartCeiling) : null;
        Raise(nameof(Chart));
    }
    private void Change(string action, Action<Project> change, bool analysis = true, bool allowArchived = false)
    {
        session.Change(action, change, analysis, allowArchived);
        selectedRow = null;
        if (analysis)
        {
            Result = null;
            Scan = null;
            Study = null;
            Comparison = null;
        }
        Rebuild();
        Message(action + "已应用；请保存项目。");
    }
}
