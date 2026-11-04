using System.ComponentModel;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Application;

public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly IDesktopInteraction interaction;
    private readonly DocumentFile file = new();
    private readonly Stack<string> undo = new();
    private readonly Stack<string> redo = new();
    private readonly List<RelayCommand> commands = [];
    private Project? current;
    private Project? dirtyReference;
    private bool cachedDirty;
    private string section = "overview";
    private string message = "从文件菜单新建空白项目，或载入独立模拟示例。";
    private bool isError;
    private string lastOutput = "";
    private string scenarioName = "";
    private Candidate? selectedCandidate;
    private SensitiveBand? selectedBand;
    private CustomOrder? selectedOrder;
    private Spectrum? selectedSpectrum;
    private Scenario? selectedScenario;
    private BandCrossing? selectedCrossing;
    private CsvImportResult? preview;
    private string previewHash = "";
    private Project? sweepContext;
    private string sweepDraftFingerprint = "";
    private SweepRequest? sweepRequest;
    private IReadOnlyList<SweepRow> sweepRows = [];
    private IReadOnlyList<ReverseRow> reverseRows = [];
    private ExportOption? selectedExport;

    public WorkspaceViewModel(IDesktopInteraction interaction)
    {
        this.interaction = interaction;
        InitializeCommands();
        HookDrafts();
        Sweep.PropertyChanged += (_, _) => Raise(nameof(SweepStatement));
        SelectedExport = ExportOptions[0];
    }

    public Project? Current => current;
    public bool HasProject => current is not null;
    public bool Editable => HasProject && !current!.Archived;
    public bool Dirty
    {
        get
        {
            if (current is null) return false;
            if (!ReferenceEquals(current, dirtyReference))
            {
                cachedDirty = file.IsDirty(current);
                dirtyReference = current;
            }
            return cachedDirty;
        }
    }
    public string FilePath => file.PathName ?? "尚未保存到文件";
    public string Title => Software.Name + " " + Software.Version + " — " + (current?.Name ?? "无项目") + (Dirty || HasDraftChanges ? " *" : "");
    public string SourceLabel => current is null ? "未建立项目" : current.Demonstration ? "模拟演示 · 非实测" : "人工项目 · 来源见各数据对象";
    public string StateLabel => current is null ? "空白工作区" : current.Archived ? "已归档 · 内容只读" : HasDraftChanges ? "有未应用输入" : Dirty ? "有未保存修改" : "已保存";
    public string Boundary => Software.Boundary;
    public string Message { get => message; private set => Set(ref message, value); }
    public bool IsError { get => isError; private set => Set(ref isError, value); }
    public string LastOutput { get => lastOutput; private set => Set(ref lastOutput, value); }
    public string Section { get => section; set => Set(ref section, value); }
    public string ScenarioName { get => scenarioName; set { Set(ref scenarioName, value); DraftChanged(); } }
    public ProjectDraft Metadata { get; private set; } = new();
    public MotorDraft Motor { get; private set; } = new();
    public OrderDraft Order { get; private set; } = new();
    public BandDraft Band { get; private set; } = new();
    public ImportDraft Import { get; private set; } = new();
    public ReviewDraft Review { get; private set; } = new();
    public SweepDraft Sweep { get; } = new();

    public bool HasAppliedDraftChanges => current is not null && (Metadata.DiffersFrom(current)
        || Motor.DiffersFrom(current.Motor)
        || Review.Tolerance != Labels.Number(current.Review.MatchToleranceHz)
        || Review.PeakCount != current.Review.MaximumPeaks.ToString(System.Globalization.CultureInfo.InvariantCulture)
        || Review.Notes != current.Review.Notes);
    public bool HasEntryDraftChanges => Order.HasInput || Band.HasInput || Import.HasInput || ScenarioName.Length > 0;
    public bool HasDraftChanges => HasProject && (HasAppliedDraftChanges || HasEntryDraftChanges);
    public Candidate? SelectedCandidate { get => selectedCandidate; set { Set(ref selectedCandidate, value); Raise(nameof(SelectedBasis)); } }
    public SensitiveBand? SelectedBand { get => selectedBand; set => Set(ref selectedBand, value); }
    public CustomOrder? SelectedOrder { get => selectedOrder; set => Set(ref selectedOrder, value); }
    public BandCrossing? SelectedCrossing { get => selectedCrossing; set => Set(ref selectedCrossing, value); }
    public Spectrum? SelectedSpectrum
    {
        get => selectedSpectrum;
        set
        {
            Set(ref selectedSpectrum, value);
            Raise(nameof(PeakMatches));
            Raise(nameof(SpectrumSummary));
            Raise(nameof(SpectrumSource));
        }
    }
    public Scenario? SelectedScenario
    {
        get => selectedScenario;
        set
        {
            Set(ref selectedScenario, value);
            Raise(nameof(Comparison));
            Raise(nameof(ComparisonStatement));
        }
    }
    public ExportOption? SelectedExport { get => selectedExport; set => Set(ref selectedExport, value); }
    public CsvImportResult? Preview => preview;
    public string PreviewStatement => preview is null ? "尚未预检；读取文件或修改数据后需要重新预检。"
        : preview.Success ? $"预检通过：{preview.ParsedRows} 点。核对元信息后确认导入。" : $"预检失败：{preview.Issues.Count} 项错误，整条频谱未导入。";
    public IReadOnlyList<ValidationIssue> ImportIssues => preview?.Issues ?? [];
    public IReadOnlyList<Candidate> Candidates => current is null ? [] : FrequencyEngine.Candidates(current.Motor, current.CustomOrders);
    public IReadOnlyList<FrequencyRow> FrequencyRows => current is null ? [] : FrequencyEngine.Rows(current, false);
    public IReadOnlyList<BandCrossing> Crossings => current is null ? [] : FrequencyEngine.Crossings(current);
    public IReadOnlyList<PeakMatch> PeakMatches => current is null || SelectedSpectrum is null ? [] : FrequencyEngine.MatchPeaks(current, SelectedSpectrum);
    public IReadOnlyList<ComparisonRow> Comparison => current is null || SelectedScenario is null ? [] : FrequencyEngine.Compare(current, SelectedScenario);
    public IReadOnlyList<string> QualityNotes => current is null ? [] : FrequencyEngine.QualityNotes(current);
    public IReadOnlyList<ReviewItem> ReviewItems => current is null ? [] : ProjectReview.Inspect(current);
    public IReadOnlyList<SweepRow> SweepRows => sweepRows;
    public IReadOnlyList<ReverseRow> ReverseRows => reverseRows;
    public string SelectedBasis => SelectedCandidate?.Basis ?? "选择候选查看公式与适用依据。";
    public string InspectSummary => current is null ? "—" : $"{Labels.Number(current.Motor.InspectRpm)} r/min · 转频 {Labels.Number(current.Motor.InspectRpm / 60)} Hz";
    public string TopologySummary => current is null ? "未设定" : Labels.Topology(current.Motor.Topology);
    public string SpectrumSource => SelectedSpectrum is null ? "未选择频谱" : $"{Labels.Origin(SelectedSpectrum.Origin)} · {SelectedSpectrum.Rpm:G6} r/min · {SelectedSpectrum.Unit} · {SelectedSpectrum.Reference}\n{SelectedSpectrum.Condition}\nSHA-256：{SelectedSpectrum.InputSha256}";
    public SpectrumSummary? SpectrumSummary => SelectedSpectrum is null || current is null ? null : SpectrumStatistics.Inspect(SelectedSpectrum, current.Motor.MaximumHz);
    public string ComparisonStatement => SelectedScenario is null || current is null ? "先记录并选择一个方案快照。"
        : "统一按当前观察转速计算；差值为当前−快照；缺项为空，不补零。"
        + (SelectedScenario.Motor.Topology != current.Motor.Topology ? " 拓扑不同，仅频率参考对照。" : "")
        + (SelectedScenario.Motor.MinimumRpm != current.Motor.MinimumRpm || SelectedScenario.Motor.MaximumRpm != current.Motor.MaximumRpm ? " 扫描范围不同，不能直接比较区间覆盖。" : "")
        + (SelectedScenario.Demonstration ? " 快照来源：模拟演示。" : "");
    public string SweepStatement => sweepContext is null ? "尚无扫查结果；参数扫查为会话工具，需单独导出。"
        : (current is not null && ParameterSweep.ContextHash(current) == ParameterSweep.ContextHash(sweepContext) && SweepFingerprint() == sweepDraftFingerprint
        ? "结果对应当前已应用电机条件。" : "当前项目或电机条件已改变；下表保留旧扫查条件，请重新运行。")
        + $" 参考转速 {sweepRequest!.ReferenceRpm:G6} r/min，目标 {sweepRequest.ObservedHz:G6} Hz，容差 {sweepRequest.ToleranceHz:G6} Hz；参数输入变更后也需重新运行。未作可制造性或优劣评价。";

    public static IReadOnlyList<NavigationItem> Navigation { get; } =
    [
        new("overview", "01  项目档案", "目标、来源与复核清单"),
        new("motor", "02  电机与转速", "有刷 / 无刷结构条件"),
        new("candidates", "03  候选依据", "机械阶次与适用假设"),
        new("bands", "04  敏感频带", "解析交叠转速区间"),
        new("spectrum", "05  频谱复核", "CSV输入与全部邻近候选"),
        new("sweep", "06  参数研究", "整数枚举与转速反算"),
        new("compare", "07  方案快照", "统一观察转速比较"),
        new("output", "08  成果输出", "本机文件与可追溯报告"),
        new("history", "09  操作记录", "输入变更与撤销重做")
    ];
    public static IReadOnlyList<Option<MotorTopology>> Topologies { get; } =
    [
        new(MotorTopology.BrushlessPermanentMagnet, "永磁无刷 · 同步运行"),
        new(MotorTopology.BrushedPermanentMagnet, "永磁有刷直流")
    ];
    public static IReadOnlyList<Option<DataOrigin>> Origins { get; } =
    [
        new(DataOrigin.Manual, "人工填写"),
        new(DataOrigin.MeasuredDeclared, "用户声明实测"),
        new(DataOrigin.Simulated, "模拟"),
        new(DataOrigin.Estimated, "估算")
    ];
    public static IReadOnlyList<string> Units { get; } = ["dB", "Pa", "m/s²", "g", "A", "V", "任意单位"];
    public static IReadOnlyList<ExportOption> ExportOptions { get; } =
    [
        new("report", "自包含复核报告", ".html", "HTML报告（仅静态成果）"),
        new("frequency", "全转速频率扫描", ".csv", "CSV表格"),
        new("crossing", "敏感频段交叠", ".csv", "CSV表格"),
        new("evidence", "全部频谱邻近核对", ".csv", "CSV表格"),
        new("comparison", "选定快照比较", ".csv", "CSV表格"),
        new("svg", "频率—转速矢量图", ".svg", "SVG矢量图"),
        new("review", "输入核对与来源", ".json", "JSON核对清单"),
        new("raw", "选定频谱原始点", ".csv", "CSV表格"),
        new("sweep", "本次参数扫查", ".csv", "CSV表格"),
        new("reverse", "本次转速反算", ".csv", "CSV表格")
    ];

    private void HookDrafts()
    {
        Metadata.PropertyChanged += OnDraftChanged;
        Motor.PropertyChanged += OnDraftChanged;
        Order.PropertyChanged += OnDraftChanged;
        Band.PropertyChanged += OnDraftChanged;
        Review.PropertyChanged += OnDraftChanged;
        Import.PropertyChanged += OnImportChanged;
    }

    private void OnImportChanged(object? sender, PropertyChangedEventArgs args)
    {
            preview = null;
            previewHash = "";
            Raise(nameof(PreviewStatement));
            Raise(nameof(ImportIssues));
            DraftChanged();
    }

    private void OnDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        DraftChanged();
    }

    private void DraftChanged()
    {
        Raise(nameof(HasDraftChanges));
        Raise(nameof(HasAppliedDraftChanges));
        Raise(nameof(StateLabel));
        Raise(nameof(Title));
    }

    private void SynchronizeDrafts(bool clearEntries)
    {
        Metadata.PropertyChanged -= OnDraftChanged;
        Motor.PropertyChanged -= OnDraftChanged;
        Order.PropertyChanged -= OnDraftChanged;
        Band.PropertyChanged -= OnDraftChanged;
        Review.PropertyChanged -= OnDraftChanged;
        Import.PropertyChanged -= OnImportChanged;
        Metadata = current is null ? new() : ProjectDraft.From(current);
        Motor = current is null ? new() : MotorDraft.From(current.Motor);
        Review = current is null ? new() : new ReviewDraft
        {
            Tolerance = Labels.Number(current.Review.MatchToleranceHz),
            PeakCount = current.Review.MaximumPeaks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Notes = current.Review.Notes
        };
        if (clearEntries)
        {
            Order = new();
            Band = new();
            Import = new();
            scenarioName = "";
        }
        HookDrafts();
    }

    private void Refresh()
    {
        foreach (var command in commands)
        {
            command.Refresh();
        }
        Raise(null);
    }

    private void Run(Action action, string? success = null)
    {
        try
        {
            action();
            if (success is not null)
            {
                Message = success;
                IsError = false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or System.Text.DecoderFallbackException)
        {
            Message = ex.Message;
            IsError = true;
        }
        Refresh();
    }
}
