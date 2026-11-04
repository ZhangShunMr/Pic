using System.IO;
using System.Globalization;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopInteraction interaction;
    private readonly ProjectSession session = new();
    private readonly List<RelayCommand> commands = [];
    private readonly List<AsyncCommand> asyncCommands = [];
    private DesignProject draft;
    private DesignVariant? selectedVariant;
    private Clip? selectedClip;
    private ContactPoint? selectedContact;
    private LoadCase? selectedLoad;
    private Vertex? selectedVertex;
    private VerificationTask? selectedTask;
    private AnalysisReport? report;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private int progress;
    private int sectionIndex;
    private int bindingErrors;
    private string status = "空白项目：请定义饰板、方案与载荷，或从文件菜单载入独立模拟示例。";
    private IReadOnlyList<SweepRow> sweepRows = [];
    private string? sweepHash;
    private string? sweptParameter;
    private string? sweptTarget;
    private LayoutImportPreview? importPreview;
    private string? importedText;
    private bool importedReplace;
    private Guid importedVariant;
    private BatchPreview? batchPreview;
    private Guid batchVariant;
    private DifferenceReport? difference;

    public WorkspaceViewModel(IDesktopInteraction interaction, string? libraryDirectory = null)
    {
        this.interaction = interaction;
        LibraryDirectory = libraryDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrimContact", "Projects");
        draft = ProjectCodec.Clone(session.Current);
        InitializeCommands();
    }

    public DesignProject Draft => draft;
    public DesignProject Applied => session.Current;
    public IReadOnlyList<DesignVariant> Variants => Draft.Variants.ToArray();
    public IReadOnlyList<Vertex> Vertices => Draft.Panel.Outline.ToArray();
    public IReadOnlyList<Clip> Clips => SelectedVariant?.Clips.ToArray() ?? [];
    public IReadOnlyList<ContactPoint> Contacts => SelectedVariant?.Contacts.ToArray() ?? [];
    public IReadOnlyList<LoadCase> Loads => Draft.Loads.ToArray();
    public IReadOnlyList<VerificationTask> Tasks => Draft.Tasks.ToArray();
    public IReadOnlyList<WorkspaceSection> Sections { get; } =
    [
        new("项目与饰板", "来源 · 轮廓 · 质量质心"),
        new("点位布置", "方案 · 卡扣 · 单边接触"),
        new("载荷与采样", "惯性载荷 · 公差角点"),
        new("求解与复核", "包络 · 控制工况 · 平衡"),
        new("比较与扫描", "同条件方案 · 单参数"),
        new("批量与导入", "事务预检 · CSV交换"),
        new("验证与追溯", "任务 · 参考文件 · 操作记录"),
        new("使用说明", "公式 · 边界 · 快速案例")
    ];
    public IReadOnlyList<OriginOption> Origins { get; } = Enum.GetValues<DataOrigin>()
        .Select(o => new OriginOption(o, o.Label())).ToArray();
    public IReadOnlyList<int> PhaseCounts { get; } = [24, 48, 72];
    public IReadOnlyList<string> SweepParameters { get; } = ["预紧量mm", "间隙mm", "法向刚度N/mm", "摩擦系数"];
    public IReadOnlyList<string> BatchTargets { get; } = ["接触点", "卡扣", "全部点位"];
    public int SectionIndex
    {
        get => sectionIndex;
        set { if (Set(ref sectionIndex, value)) Raise(nameof(Section)); }
    }
    public WorkspaceSection Section => Sections[SectionIndex];
    public string Status { get => status; private set => Set(ref status, value); }
    public int Progress { get => progress; private set => Set(ref progress, value); }
    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (!Set(ref busy, value)) return;
            Raise(nameof(CanEdit));
            Raise(nameof(CanNavigate));
            RefreshCommands();
        }
    }
    public bool CanNavigate => !IsBusy && !HasConversionErrors;
    public bool CanEdit => !IsBusy && !Applied.Archived;
    public bool HasConversionErrors => bindingErrors > 0;
    private static readonly System.Text.Json.JsonSerializerOptions DraftOptions = new(ProjectCodec.Options)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    public bool DraftDirty => System.Text.Json.JsonSerializer.Serialize(Draft, DraftOptions)
        != System.Text.Json.JsonSerializer.Serialize(Applied, DraftOptions);
    public bool HasUnsavedChanges => DraftDirty || session.Dirty || HasConversionErrors;
    public bool ResultsCurrent => report is not null && !DraftDirty && !HasConversionErrors
        && report.InputHash == ProjectCodec.InputHash(Applied);
    public bool SweepCurrent => sweepRows.Count > 0 && !DraftDirty && !HasConversionErrors
        && sweepHash == ProjectCodec.InputHash(Applied);
    public double PlotHeight => Math.Max(320, Draft.Loads.Count * 250);
    public string ResultState => ResultsCurrent ? "结果有效 · " + report!.CalculatedUtc.ToLocalTime().ToString("g")
        : "尚无有效结果，应用输入后重新计算";
    public string ProjectState => Applied.Archived ? "归档只读 · 复制项目后可编辑"
        : HasConversionErrors ? "数值格式错误：修正红框字段后再应用"
        : DraftDirty ? "草稿待应用 · 结果暂不可用"
        : session.Dirty ? "已应用 · 尚未保存文件" : "文件与工作副本一致";
    public string FileLocation => session.SavedPath ?? "未关联文件";
    public string Title => Product.Name + " " + Product.Version + " — " + Draft.Name;
    public string SourceLabel => Draft.Origin.Label() + " · " + Draft.Source;
    public string Boundary => Product.Boundary;
    public IReadOnlyList<VariantResult> VariantResults => ResultsCurrent ? report!.Variants : [];
    public IReadOnlyList<PointEnvelope> Envelopes => ResultsCurrent
        ? report!.Variants.FirstOrDefault(v => v.VariantId == SelectedVariant?.Id)?.Contacts
            .Concat(report.Variants.First(v => v.VariantId == SelectedVariant?.Id).Clips).ToArray() ?? [] : [];
    public IReadOnlyList<BalanceRow> BalanceRows => ResultsCurrent ? BalanceAudit.Evaluate(Applied, report!) : [];
    public IReadOnlyList<ComparisonRow> Comparisons => ResultsCurrent ? AnalysisEngine.Compare(report!) : [];
    public IReadOnlyList<QualityFinding> QualityRows => QualityInspector.Inspect(Applied, ResultsCurrent ? report : null);
    public IReadOnlyList<LayoutMetric> LayoutMetrics => QualityInspector.Metrics(Applied);
    public IReadOnlyList<SweepRow> SweepRows => SweepCurrent ? sweepRows : [];
    public IReadOnlyList<InputDifference> Differences => !DraftDirty && !HasConversionErrors ? difference?.Rows ?? [] : [];
    public string DifferenceSummary => DraftDirty || HasConversionErrors ? "输入尚未应用，请应用后重新核对参考项目。"
        : difference is null ? "选择参考项目，只核对输入，不进行跨条件结果归因。"
        : difference.ReferenceName + "：" + difference.Rows.Count + "项差异；共同条件"
            + (difference.SameSharedConditions ? "一致" : "不同，禁止直接归因");
    public IReadOnlyList<BatchChange> BatchChanges => batchPreview?.Changes ?? [];
    public IReadOnlyList<string> ImportMessages => importPreview?.Messages ?? [];
    public string ImportCsvText { get; set; } = "";
    public bool ImportReplace { get; set; }
    public BatchLayoutRequest BatchRequest { get; set; } = new();
    public string SweepParameter { get; set; } = "预紧量mm";
    public double SweepFrom { get; set; }
    public double SweepTo { get; set; } = 0.6;
    public int SweepSteps { get; set; } = 6;
    public double RectangleWidth { get; set; } = 400;
    public double RectangleHeight { get; set; } = 300;
    public double PlacementX { get; set; } = 200;
    public double PlacementY { get; set; } = 150;

    public DesignVariant? SelectedVariant
    {
        get => selectedVariant;
        set
        {
            if (!Set(ref selectedVariant, value)) return;
            SelectedClip = value?.Clips.FirstOrDefault();
            SelectedContact = value?.Contacts.FirstOrDefault();
            Raise(nameof(Envelopes));
            Raise(nameof(Clips));
            Raise(nameof(Contacts));
            Raise(nameof(SelectedVariantSummary));
            RefreshCommands();
        }
    }
    public string SelectedVariantSummary => SelectedVariant is null ? "先建立方案"
        : $"{SelectedVariant.Name} · {SelectedVariant.Clips.Count}卡扣 / {SelectedVariant.Contacts.Count}接触点";
    public Clip? SelectedClip
    {
        get => selectedClip;
        set { if (Set(ref selectedClip, value)) RefreshCommands(); }
    }
    public ContactPoint? SelectedContact
    {
        get => selectedContact;
        set { if (Set(ref selectedContact, value)) RefreshCommands(); }
    }
    public LoadCase? SelectedLoad
    {
        get => selectedLoad;
        set { if (Set(ref selectedLoad, value)) RefreshCommands(); }
    }
    public Vertex? SelectedVertex
    {
        get => selectedVertex;
        set { if (Set(ref selectedVertex, value)) RefreshCommands(); }
    }
    public VerificationTask? SelectedTask
    {
        get => selectedTask;
        set { if (Set(ref selectedTask, value)) RefreshCommands(); }
    }

    public void SetBindingErrors(int count)
    {
        bindingErrors = Math.Max(0, count);
        NotifyDraftEdited();
    }

    public void NotifyDraftEdited()
    {
        foreach (var property in new[] { nameof(DraftDirty), nameof(HasUnsavedChanges), nameof(HasConversionErrors),
            nameof(ResultsCurrent), nameof(SweepCurrent), nameof(ResultState), nameof(ProjectState), nameof(Title),
            nameof(SourceLabel), nameof(CanNavigate), nameof(Differences), nameof(DifferenceSummary), nameof(Envelopes), nameof(VariantResults), nameof(Comparisons), nameof(BalanceRows),
            nameof(SweepRows), nameof(PlotHeight), nameof(SweepDescription), nameof(SelectedVariantSummary) })
            Raise(property);
        RefreshCommands();
    }

    public event EventHandler? DraftReplaced;

    private void ReloadDraft(Guid? variantId = null)
    {
        bindingErrors = 0;
        DraftReplaced?.Invoke(this, EventArgs.Empty);
        var id = variantId ?? SelectedVariant?.Id;
        draft = ProjectCodec.Clone(Applied);
        selectedVariant = null;
        selectedClip = null;
        selectedContact = null;
        SelectedVariant = Draft.Variants.FirstOrDefault(v => v.Id == id) ?? Draft.Variants.FirstOrDefault();
        SelectedLoad = Draft.Loads.FirstOrDefault();
        SelectedVertex = Draft.Panel.Outline.FirstOrDefault();
        SelectedTask = Draft.Tasks.FirstOrDefault();
        difference = null;
        importPreview = null;
        batchPreview = null;
        foreach (var property in new[] { nameof(Draft), nameof(Applied), nameof(FileLocation), nameof(CanEdit),
            nameof(QualityRows), nameof(LayoutMetrics), nameof(Differences), nameof(DifferenceSummary),
            nameof(ImportMessages), nameof(BatchChanges), nameof(Variants), nameof(Vertices), nameof(Loads), nameof(Tasks),
            nameof(Clips), nameof(Contacts), nameof(SelectedClip), nameof(SelectedContact), nameof(SelectedVariant) }) Raise(property);
        NotifyDraftEdited();
    }

    private RelayCommand Command(Action action, Func<bool>? allowed = null)
    {
        var command = new RelayCommand(() => Guard(action), allowed);
        commands.Add(command);
        return command;
    }

    private AsyncCommand BackgroundCommand(Func<Task> action, Func<bool>? allowed = null)
    {
        var command = new AsyncCommand(action, allowed);
        asyncCommands.Add(command);
        return command;
    }

    private void RefreshCommands()
    {
        foreach (var command in commands) command.Refresh();
        foreach (var command in asyncCommands) command.Refresh();
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            Status = "操作未完成：" + error.Message;
            interaction.Error(Status);
        }
    }

    private static string Limited(string value, int length) => value.Length > length ? value[..length] : value;
    private static string N(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    public void Dispose()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
