using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;
using Cabin.Core.IO;
using Cabin.Desktop.Infrastructure;
using Cabin.Desktop.Services;

namespace Cabin.Desktop.ViewModels;

public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly IDesktopServices services;
    private ProjectEditor? editor;
    private Project? computedProject;
    private AnalysisResult? result;
    private FieldResult? field;
    private ComparisonResult? comparison;
    private ScanResult? scan;
    private TruncationStudy? truncation;
    private string section = "project";
    private string status = "新建空白项目，或通过文件菜单载入独立模拟示例。";
    private string path = "";
    private bool dirty;
    private bool loading;
    private bool busy;
    private int progress;
    private CancellationTokenSource? cancellation;
    private PointEditor? selectedSource;
    private PointEditor? selectedSeat;
    private SeatCurve? selectedCurve;
    private Mode? selectedMode;
    private Snapshot? selectedBaseline;
    private ReviewEditor? selectedReview;
    private string baselineName = "方案基准";
    private double frequency = 50;
    private double slice = 0.5;
    private Choice plane;
    private bool showShape;
    private Choice scanParameter;
    private ScanRow? selectedScanRow;
    private IReadOnlyList<Contribution> contributions = Array.Empty<Contribution>();
    private IReadOnlyList<CouplingDiagnostic> coupling = Array.Empty<CouplingDiagnostic>();
    private IReadOnlyList<DensityBand> density = Array.Empty<DensityBand>();
    private IReadOnlyList<string> advice = Array.Empty<string>();
    private PlotData? spectrum;
    private PlotData? scanPlot;

    public WorkspaceViewModel(IDesktopServices services)
    {
        this.services = services;
        plane = Planes[0];
        scanParameter = ScanParameters[0];
        ScanStart = InputField.Number("扫描起值", 3.0, 0.002, 10, ClearScan, "尺寸/坐标单位m；阻尼无量纲");
        ScanEnd = InputField.Number("扫描终值", 4.0, 0.002, 10, ClearScan, "大于起值，扫描时保持绝对点位");
        ScanCount = InputField.Number("候选数", 7, 2, 21, ClearScan, "整数2—21");
        NewCommand = Command(NewProject, () => !Busy);
        ExampleCommand = Command(OpenExample, () => !Busy);
        OpenCommand = Command(OpenProject, () => !Busy);
        SaveCommand = Command(() => Save(false), () => HasProject && !Busy);
        SaveAsCommand = Command(() => Save(true), () => HasProject && !Busy);
        DuplicateCommand = Command(Duplicate, () => HasProject && !Busy);
        AddSourceCommand = Command(() => Editor!.AddSource(), Editable);
        AddSeatCommand = Command(() => Editor!.AddSeat(), Editable);
        DeleteSourceCommand = Command(DeleteSource, () => Editable() && SelectedSource is not null);
        DeleteSeatCommand = Command(DeleteSeat, () => Editable() && SelectedSeat is not null);
        ImportCommand = Command(ImportPoints, Editable);
        ExportPointsCommand = Command(ExportPoints, Editable);
        ValidateCommand = Command(ValidateInputs, Editable);
        CalculateCommand = new AsyncCommand(Calculate, Editable);
        CancelCommand = Command(() => cancellation?.Cancel(), () => Busy);
        UpdateFieldCommand = new AsyncCommand(UpdateField, HasResult);
        CaptureBaselineCommand = Command(CaptureBaseline, HasResult);
        CompareCommand = new AsyncCommand(Compare, () => HasResult() && SelectedBaseline is not null);
        RestoreBaselineCommand = Command(RestoreBaseline, () => Editable() && SelectedBaseline is not null);
        DeleteBaselineCommand = Command(DeleteBaseline, () => Editable() && SelectedBaseline is not null);
        ScanCommand = new AsyncCommand(RunScan, HasResult);
        ApplyScanCommand = Command(ApplyScan, () => HasResult() && SelectedScanRow is { Error.Length: 0 });
        TruncationCommand = new AsyncCommand(ExtendTruncation, HasResult);
        AddReviewCommand = Command(AddReview, Editable);
        DeleteReviewCommand = Command(DeleteReview, () => Editable() && SelectedReview is not null);
        ExportReportCommand = Command(() => Export("研究报告", ".txt", ResearchExports.Report(computedProject!, Result!, Comparison, Scan, Truncation)), HasResult);
        ExportResponsesCommand = Command(() => Export("座位复数频响", ".csv", ResearchExports.Responses(computedProject!, Result!)), HasResult);
        ExportModesCommand = Command(() => Export("模态清单", ".csv", ResearchExports.ModeLedger(computedProject!, Result!)), HasResult);
        ExportFieldCommand = Command(() => Export("剖面数据", ".csv", ResearchExports.Field(computedProject!, Field!, Frequency, ShowShape ? SelectedMode : null)), () => HasResult() && Field is not null);
        ExportScanCommand = Command(() => Export("参数扫描", ".csv", ResearchExports.Scan(computedProject!, Scan!)), () => HasResult() && Scan is not null);
        ExportComparisonCommand = Command(() => Export("基准差异", ".csv", ResearchExports.Differences(computedProject!, Comparison!)), () => HasResult() && Comparison is not null);
        ExportImageCommand = Command(services.ExportFieldImage, () => HasResult() && Field is not null && Section == "field");
    }

    public IReadOnlyList<Choice> Planes { get; } = new[]
    {
        new Choice("xy", "XY 水平剖面"),
        new Choice("xz", "XZ 纵向剖面"),
        new Choice("yz", "YZ 横向剖面")
    };

    public IReadOnlyList<Choice> ScanParameters { get; } = new[]
    {
        new Choice("length", "长度 Lx / m"),
        new Choice("width", "宽度 Ly / m"),
        new Choice("height", "高度 Lz / m"),
        new Choice("sourceX", "首个声源 x / m"),
        new Choice("sourceY", "首个声源 y / m"),
        new Choice("sourceZ", "首个声源 z / m"),
        new Choice("damping", "等效阻尼比")
    };

    public ProjectEditor? Editor { get => editor; private set => Set(ref editor, value); }
    public AnalysisResult? Result { get => result; private set => Set(ref result, value); }
    public FieldResult? Field { get => field; private set => Set(ref field, value); }
    public ComparisonResult? Comparison { get => comparison; private set => Set(ref comparison, value); }
    public ScanResult? Scan { get => scan; private set => Set(ref scan, value); }
    public TruncationStudy? Truncation { get => truncation; private set => Set(ref truncation, value); }
    public IReadOnlyList<Contribution> Contributions { get => contributions; private set => Set(ref contributions, value); }
    public IReadOnlyList<CouplingDiagnostic> Coupling { get => coupling; private set => Set(ref coupling, value); }
    public IReadOnlyList<DensityBand> Density { get => density; private set => Set(ref density, value); }
    public IReadOnlyList<string> Advice { get => advice; private set => Set(ref advice, value); }
    public PlotData? Spectrum { get => spectrum; private set => Set(ref spectrum, value); }
    public PlotData? ScanPlot { get => scanPlot; private set => Set(ref scanPlot, value); }
    public ObservableCollection<Snapshot> Baselines { get; } = new();
    public ObservableCollection<ReviewEditor> Reviews { get; } = new();
    public ObservableCollection<JournalEntry> Journal { get; } = new();
    public InputField ScanStart { get; }
    public InputField ScanEnd { get; }
    public InputField ScanCount { get; }
    public Project? ComputedProject => computedProject;
    public bool NotBusy => !Busy;
    public bool HasProject => Editor is not null;
    public bool CanEdit => HasProject && !Busy;
    public bool ResultAvailable => Result is not null;
    public string Scope => Software.Scope;
    public string Identity => Software.Name + " V" + Software.Version;
    public string Title => (Dirty ? "* " : "") + (Editor?.Name.Text ?? "未建立项目") + " — 座舱模态工作台";
    public string FileLocation => string.IsNullOrEmpty(path) ? "尚未保存到文件" : path;
    public string ResultStatus => Result is null ? "未计算 / 输入变更后需重算" : $"已计算 {Result.Computed.LocalDateTime:HH:mm:ss} · {Result.Modes.Count} 个非零模态";
    public string FieldHint => ShowShape ? "振型：蓝=负，白=节点，橙=正；范围[-1,1]。" : "响应幅值：浅=低，深=高；按当前剖面最大值归一化。";
    public string SelectionHint => $"{Plane.Label} · 固定轴位置 {Slice:P0} · {Frequency:F2} Hz";
    public bool Dirty { get => dirty; private set { if (Set(ref dirty, value)) Notify(nameof(Title)); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public int Progress { get => progress; private set => Set(ref progress, value); }
    public bool Busy
    {
        get => busy;
        private set
        {
            if (!Set(ref busy, value))
                return;
            Notify(nameof(CanEdit));
            Notify(nameof(NotBusy));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Section
    {
        get => section;
        set
        {
            if (Set(ref section, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public PointEditor? SelectedSource { get => selectedSource; set => Set(ref selectedSource, value); }
    public PointEditor? SelectedSeat { get => selectedSeat; set { if (Set(ref selectedSeat, value)) ClearScan(); } }
    public SeatCurve? SelectedCurve { get => selectedCurve; set { if (Set(ref selectedCurve, value)) InvalidateField(); } }
    public Mode? SelectedMode { get => selectedMode; set { if (Set(ref selectedMode, value)) InvalidateField(); } }
    public Snapshot? SelectedBaseline { get => selectedBaseline; set { if (Set(ref selectedBaseline, value)) Comparison = null; } }
    public ReviewEditor? SelectedReview { get => selectedReview; set => Set(ref selectedReview, value); }
    public ScanRow? SelectedScanRow { get => selectedScanRow; set => Set(ref selectedScanRow, value); }
    public string BaselineName { get => baselineName; set => Set(ref baselineName, value); }
    public Choice Plane { get => plane; set { if (Set(ref plane, value)) InvalidateField(); } }
    public Choice ScanParameter { get => scanParameter; set { if (Set(ref scanParameter, value)) ClearScan(); } }
    public bool ShowShape { get => showShape; set { if (Set(ref showShape, value)) InvalidateField(); } }
    public double Frequency
    {
        get => frequency;
        set
        {
            if (!double.IsFinite(value))
                return;
            var m = computedProject?.Model;
            var next = Math.Clamp(value, m?.MinimumFrequency ?? 5, m?.MaximumFrequency ?? 500);
            if (Set(ref frequency, next))
                InvalidateField();
        }
    }

    public double Slice
    {
        get => slice;
        set
        {
            if (double.IsFinite(value) && Set(ref slice, Math.Clamp(value, 0, 1)))
                InvalidateField();
        }
    }

    public ICommand NewCommand { get; }
    public ICommand ExampleCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand AddSeatCommand { get; }
    public ICommand DeleteSourceCommand { get; }
    public ICommand DeleteSeatCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportPointsCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand CalculateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand UpdateFieldCommand { get; }
    public ICommand CaptureBaselineCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand RestoreBaselineCommand { get; }
    public ICommand DeleteBaselineCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand ApplyScanCommand { get; }
    public ICommand TruncationCommand { get; }
    public ICommand AddReviewCommand { get; }
    public ICommand DeleteReviewCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand ExportResponsesCommand { get; }
    public ICommand ExportModesCommand { get; }
    public ICommand ExportFieldCommand { get; }
    public ICommand ExportScanCommand { get; }
    public ICommand ExportComparisonCommand { get; }
    public ICommand ExportImageCommand { get; }

    public bool CanClose()
    {
        if (Busy)
        {
            services.Error("计算正在进行。请先点击状态栏“取消计算”，等待结束后再关闭。");
            return false;
        }
        try
        {
            return CheckDiscard();
        }
        catch (Exception error) when (Expected(error))
        {
            services.Error(error.Message);
            return false;
        }
    }

    private bool Editable() => HasProject && !Busy;
    private bool HasResult() => Result is not null && !Busy;

    private RelayCommand Command(Action action, Func<bool>? allowed = null)
    {
        return new(() => Guard(action), allowed);
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (Expected(error))
        {
            Status = "操作未完成：" + error.Message;
            services.Error(error.Message);
        }
    }

    private static bool Expected(Exception error)
    {
        return error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.DecoderFallbackException;
    }

    private void Load(Project project, string filePath, bool changed)
    {
        loading = true;
        try
        {
            Editor = new(project, Changed);
            Baselines.Clear();
            foreach (var item in project.Baselines)
                Baselines.Add(item);
            Reviews.Clear();
            foreach (var item in project.Reviews)
                Reviews.Add(new(item, Changed));
            Journal.Clear();
            foreach (var item in project.Journal)
                Journal.Add(item);
            path = filePath;
            SelectedSource = Editor.Sources.FirstOrDefault();
            SelectedSeat = Editor.Seats.FirstOrDefault();
            SelectedBaseline = Baselines.FirstOrDefault();
            SelectedReview = Reviews.FirstOrDefault();
            ClearResults();
            Dirty = changed;
            Notify(nameof(HasProject));
            Notify(nameof(CanEdit));
            Notify(nameof(FileLocation));
            Notify(nameof(Title));
        }
        finally
        {
            loading = false;
        }
    }

    private void Changed()
    {
        if (loading)
            return;
        Dirty = true;
        Notify(nameof(Title));
        ClearResults();
        Status = "输入已更改。请校验输入并重新计算。";
    }

    private void ClearResults()
    {
        Result = null;
        computedProject = null;
        Notify(nameof(ComputedProject));
        Field = null;
        Comparison = null;
        Scan = null;
        Truncation = null;
        Spectrum = null;
        ScanPlot = null;
        Contributions = Array.Empty<Contribution>();
        Coupling = Array.Empty<CouplingDiagnostic>();
        Density = Array.Empty<DensityBand>();
        Advice = Array.Empty<string>();
        SelectedCurve = null;
        SelectedMode = null;
        Notify(nameof(ResultAvailable));
        Notify(nameof(ResultStatus));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearScan()
    {
        Scan = null;
        ScanPlot = null;
        SelectedScanRow = null;
    }

    private void InvalidateField()
    {
        Field = null;
        Contributions = Array.Empty<Contribution>();
        Coupling = Array.Empty<CouplingDiagnostic>();
        Notify(nameof(FieldHint));
        Notify(nameof(SelectionHint));
        CommandManager.InvalidateRequerySuggested();
    }

    private Project Build(bool calculation)
    {
        var project = Editor!.Build(false);
        project.Baselines = Baselines.Select(ProjectCodec.Clone).ToList();
        project.Reviews = Reviews.Select(x => x.Build()).ToList();
        project.Journal = Journal.ToList();
        var issues = Validation.Check(project, calculation);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, issues.Select(x => x.Path + "：" + x.Message)));
        return project;
    }

    private bool CheckDiscard()
    {
        if (!Dirty)
            return true;
        var decision = services.SaveChanges(Editor?.Name.Text ?? "当前项目");
        if (decision == 0)
            return false;
        return decision == 2 || Save(false);
    }

    private void NewProject()
    {
        if (!CheckDiscard())
            return;
        Load(new Project(), "", true);
        Section = "project";
        Status = "空白项目已建立。默认几何仅为起点，请填写来源并添加声源和耳点。";
    }

    private void OpenExample()
    {
        var choice = services.SelectExample();
        if (choice == 0 || !CheckDiscard())
            return;
        var project = Demo.Create();
        if (choice == 2)
        {
            project.Name = "双源反相抵消 · 模拟研究";
            project.Sources.Add(ProjectCodec.Clone(project.Sources[0]));
            project.Sources[1].Id = Guid.NewGuid();
            project.Sources[1].Name = "同点反相声源";
            project.Sources[1].PhaseDegrees = 180;
            project.Baselines.Clear();
            project.Notes = "两个同点同幅反相声源理论相消；剩余为浮点舍入误差。非实测数据。";
        }
        Load(project, "", true);
        Section = "project";
        Status = "独立模拟示例已载入。点击“计算相对声场”生成结果。";
    }

    private void OpenProject()
    {
        var nextPath = services.OpenProjectPath();
        if (nextPath is null)
            return;
        var project = NativeFiles.Open(nextPath);
        if (!CheckDiscard())
            return;
        Load(project, nextPath, false);
        Section = "project";
        Status = "项目已打开，全部输入已恢复；计算结果需重新生成。";
    }

    private bool Save(bool saveAs)
    {
        var project = Build(false);
        var target = path;
        if (saveAs || string.IsNullOrEmpty(target))
            target = services.SaveProjectPath(path, project.Name) ?? "";
        if (string.IsNullOrEmpty(target))
            return false;
        JournalEntry entry = new() { Action = "保存项目", Detail = "版本" + project.Revision + "；内嵌全部点位、基准与复核" };
        project.Journal.Add(entry);
        while (project.Journal.Count > 500)
            project.Journal.RemoveAt(0);
        NativeFiles.Save(target, project);
        _ = NativeFiles.Open(target);
        Journal.Clear();
        foreach (var item in project.Journal)
            Journal.Add(item);
        path = target;
        if (computedProject is not null)
        {
            computedProject.Journal = ProjectCodec.Clone(project.Journal);
            computedProject.Modified = project.Modified;
        }
        Dirty = false;
        Notify(nameof(FileLocation));
        Status = "已保存并重开校验：" + target;
        return true;
    }

    private void Duplicate()
    {
        var project = Build(false);
        if (!CheckDiscard())
            return;
        project.Id = Guid.NewGuid();
        project.Name = project.Name.Length > 72 ? project.Name[..72] + " 副本" : project.Name + " 副本";
        project.Created = DateTimeOffset.UtcNow;
        project.Journal.Add(new() { Action = "复制项目", Detail = "新项目标识；保留点位和基准标识以便比较" });
        if (project.Journal.Count > 500)
            project.Journal.RemoveAt(0);
        Load(project, "", true);
        Status = "已创建独立副本，请另存为新的项目文件。";
    }

    private void DeleteSource()
    {
        if (!services.Confirm("删除声源", "删除所选声源“" + SelectedSource!.Name.Text + "”？"))
            return;
        Editor!.Sources.Remove(SelectedSource);
        SelectedSource = Editor.Sources.FirstOrDefault();
        Changed();
    }

    private void DeleteSeat()
    {
        if (!services.Confirm("删除耳点", "删除所选耳点“" + SelectedSeat!.Name.Text + "”？基准保留原耳点。"))
            return;
        Editor!.Seats.Remove(SelectedSeat);
        SelectedSeat = Editor.Seats.FirstOrDefault();
        Changed();
    }

    private void ValidateInputs()
    {
        var project = Build(true);
        _ = Modes.Enumerate(project.Model);
        Status = "输入校验通过。请计算相对声场；输入有效不表示实车模型已验证。";
    }

    private void ImportPoints()
    {
        var target = services.OpenCsvPath();
        if (target is null)
            return;
        var project = Build(false);
        var preview = PointCsv.Preview(NativeFiles.ReadCsv(target), project.Model);
        if (!services.ConfirmImport(preview))
            return;
        PointCsv.Apply(project, preview);
        Load(project, path, true);
        Section = "points";
        Status = "点位CSV已通过预检并替换当前点位；请核对来源、声源启停和重新计算。";
    }

    private void ExportPoints()
    {
        var project = Build(false);
        Export("点位交换", ".csv", PointCsv.Export(project));
    }

    private async Task Work(string message, Func<CancellationToken, Task> action)
    {
        Busy = true;
        Progress = 0;
        Status = message;
        cancellation = new();
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "计算已取消，未应用未完成的结果。";
        }
        catch (Exception error) when (Expected(error))
        {
            Status = "计算未完成：" + error.Message;
            services.Error(error.Message);
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
            Busy = false;
        }
    }

    private async Task Calculate()
    {
        await Work("正在计算非零阶模态与座位复数响应…", async token =>
        {
            var project = Build(true);
            var calculated = await Task.Run(() => Engine.Calculate(project, token), token);
            token.ThrowIfCancellationRequested();
            computedProject = project;
            Notify(nameof(ComputedProject));
            Result = calculated;
            SelectedCurve = Result.Curves.FirstOrDefault();
            SelectedMode = Result.Modes.FirstOrDefault();
            Frequency = Math.Clamp(Frequency, project.Model.MinimumFrequency, project.Model.MaximumFrequency);
            Spectrum = BuildSpectrum(Result);
            Density = Diagnostics.Density(Result);
            Advice = Diagnostics.Advice(project, Result);
            Notify(nameof(ResultAvailable));
            Notify(nameof(ResultStatus));
            Section = "field";
            await RefreshField(token);
            Status = $"计算完成：{Result.Modes.Count}个模态，{Result.Frequencies.Count}个频率样点。结果为理想模型相对量。";
        });
    }

    private async Task UpdateField()
    {
        await Work("正在更新剖面及所选耳点贡献…", async token =>
        {
            await RefreshField(token);
            Status = "剖面与贡献已更新。";
        });
    }

    private async Task RefreshField(CancellationToken token)
    {
        if (computedProject is null || Result is null)
            return;
        var project = computedProject;
        var calculated = Result;
        var selectedFrequency = Frequency;
        var selectedPlane = Plane.Key;
        var selectedSlice = Slice;
        var shape = ShowShape;
        var mode = SelectedMode;
        if (shape && mode is null)
            throw new ArgumentException("截断内没有模态可显示振型，请使用响应幅值或提高截断频率");
        var seat = project.Seats.FirstOrDefault(x => x.Id == SelectedCurve?.Id);
        var next = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return Engine.Field(project, calculated, selectedPlane, selectedSlice, shape, mode, selectedFrequency);
        }, token);
        token.ThrowIfCancellationRequested();
        Field = next;
        Contributions = seat is null ? Array.Empty<Contribution>() : Engine.Contributions(project, calculated, seat, selectedFrequency);
        Coupling = seat is null ? Array.Empty<CouplingDiagnostic>() : Diagnostics.Coupling(project, calculated, seat);
    }

    private void CaptureBaseline()
    {
        if (Baselines.Count >= 12)
            throw new ArgumentException("最多保留12个基准，请先删除不需要的基准");
        if (string.IsNullOrWhiteSpace(BaselineName) || BaselineName.Length > 80)
            throw new ArgumentException("基准名称必须为1—80字符");
        var snapshot = Demo.Capture(computedProject!, BaselineName);
        Baselines.Add(snapshot);
        SelectedBaseline = snapshot;
        Dirty = true;
        Status = "已从当前计算输入捕获基准；请保存项目以保留。";
    }

    private async Task Compare()
    {
        await Work("正在复算基准并匹配耳点…", async token =>
        {
            var baseline = SelectedBaseline!;
            var next = await Task.Run(() => Cabin.Core.Analysis.Comparison.Compare(computedProject!, Result!, baseline), token);
            token.ThrowIfCancellationRequested();
            Comparison = next;
            Status = "基准比较完成。差值为相对响应比值，请同时阅读条件变化提示。";
        });
    }

    private void RestoreBaseline()
    {
        var project = Build(false);
        if (!services.Confirm("恢复基准输入", "以选中基准替换当前几何、来源、声源和耳点？此操作会清除当前计算结果。"))
            return;
        var baseline = SelectedBaseline!;
        project.Model = ProjectCodec.Clone(baseline.Model);
        project.Sources = ProjectCodec.Clone(baseline.Sources);
        project.Seats = ProjectCodec.Clone(baseline.Seats);
        project.IsDemo = baseline.IsDemo;
        project.DataSource = baseline.DataSource;
        Load(project, path, true);
        Status = "已恢复基准输入。请重新计算。";
    }

    private void DeleteBaseline()
    {
        if (!services.Confirm("删除基准", "删除选中基准？保存后将从项目文件中移除。"))
            return;
        Baselines.Remove(SelectedBaseline!);
        SelectedBaseline = Baselines.FirstOrDefault();
        Dirty = true;
    }

    private async Task RunScan()
    {
        await Work("正在扫描参数…", async token =>
        {
            var count = ScanCount.Value;
            if (count != Math.Truncate(count))
                throw new ArgumentException("扫描候选数必须为整数");
            var settings = new ScanSettings(ScanParameter.Key, ScanStart.Value, ScanEnd.Value, (int)count,
                SelectedSeat?.Id ?? Guid.Empty);
            var reporter = new Progress<int>(value => Progress = value);
            var next = await Task.Run(() => ParameterScan.Run(computedProject!, settings, token, reporter), token);
            token.ThrowIfCancellationRequested();
            Scan = next;
            ScanPlot = new("扫描耳点的采样RMS", ScanParameter.Label, "相对响应 / 无量纲", new[]
            {
                new PlotLine("有效候选", "#167D8D", next.Rows.Where(x => x.Error.Length == 0).Select(x => new PlotPoint(x.Parameter, x.Rms)).ToArray())
            });
            SelectedScanRow = next.Rows.Where(x => x.Error.Length == 0).MinBy(x => x.Rms);
            Status = $"扫描完成；{next.Rows.Count(x => x.Error.Length == 0)}个有效候选。最低值仅对当前选定耳点、频带和采样成立。";
        });
    }

    private void ApplyScan()
    {
        if (Scan is null || SelectedScanRow is null)
            return;
        if (!services.Confirm("应用扫描候选", "将所选参数值应用到当前项目？其他点位保持绝对坐标，需要重新计算。"))
            return;
        var project = Build(false);
        ParameterScan.Apply(project, Scan.Settings.Parameter, SelectedScanRow.Parameter);
        Load(project, path, true);
        Status = "扫描候选已应用。请重新计算并比较全部座位。";
    }

    private async Task ExtendTruncation()
    {
        await Work("正在增加100Hz截断并比较数值敏感性…", async token =>
        {
            var next = await Task.Run(() => Diagnostics.Extend(computedProject!, Result!), token);
            token.ThrowIfCancellationRequested();
            Truncation = next;
            Status = "截断敏感性计算完成；该检查不改变项目参数。";
        });
    }

    private void AddReview()
    {
        if (Reviews.Count >= 100)
            throw new ArgumentException("复核清单最多100项");
        var item = new ReviewEditor(new ReviewItem
        {
            Target = "待复核对象",
            Action = "填写实测或高保真仿真复核步骤",
            Result = "待验证"
        }, Changed);
        Reviews.Add(item);
        SelectedReview = item;
        Changed();
    }

    private void DeleteReview()
    {
        if (!services.Confirm("删除复核项", "删除选中的工程复核记录？"))
            return;
        Reviews.Remove(SelectedReview!);
        SelectedReview = Reviews.FirstOrDefault();
        Changed();
    }

    private void Export(string label, string extension, string text)
    {
        var target = services.ExportPath(label, extension);
        if (target is null)
            return;
        NativeFiles.WriteText(target, text);
        Status = "已导出：" + target;
    }

    private static PlotData BuildSpectrum(AnalysisResult calculated)
    {
        var colors = new[] { "#147D92", "#C06A32", "#7364AB", "#398056", "#AF426A", "#6A6A30" };
        var lines = calculated.Curves.Select((curve, i) => new PlotLine(curve.Name, colors[i % colors.Length],
            curve.Points.Select(x => new PlotPoint(x.Frequency, x.RelativeDb(calculated.Reference))).ToArray())).ToArray();
        return new("座位频响 · 公共全频带最大值为0dB参考", "频率 / Hz", "相对幅值 / dB（非dB SPL）", lines);
    }
}
