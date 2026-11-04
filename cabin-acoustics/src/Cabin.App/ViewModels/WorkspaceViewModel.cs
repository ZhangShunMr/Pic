using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;
using Cabin.Core.IO;

namespace Cabin.App.ViewModels;

public sealed class WorkspaceViewModel(ProjectStore store, DownloadVault vault) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Project? Current { get; private set; }
    public AnalysisResult? Result { get; private set; }
    public ComparisonResult? Difference { get; private set; }
    public ScanResult? Scan { get; private set; }
    public TruncationStudy? Truncation { get; private set; }
    public CsvPreview? Csv { get; private set; }
    public List<StoredProject> Library { get; private set; } = [];
    public List<Issue> Issues { get; private set; } = [];
    public string Status { get; private set; } = "从空白项目开始，或载入独立模拟示例。";
    public string DownloadUrl { get; private set; } = "";
    public string DownloadName { get; private set; } = "";
    public string Confirmation { get; private set; } = "";
    public bool Dirty { get; private set; }
    public bool FileOpen { get; set; }
    public bool SettingsOpen { get; set; }
    public bool HelpOpen { get; set; }
    public bool Busy { get; private set; }
    public string Plane { get; set; } = "xy";
    public double Slice { get; set; } = 0.7;
    public string Quantity { get; set; } = "shape";
    public double CursorFrequency { get; set; } = 50;
    public string SelectedModeKey { get; set; } = "";
    public Guid SelectedSeatId { get; set; }
    public Guid SelectedSourceId { get; set; }
    public Guid BaselineId { get; set; }
    public string BaselineName { get; set; } = "新基准";
    public string ModeFilter { get; set; } = "全部";
    public string PointAction { get; set; } = "select";
    public string CsvText { get; set; } = PointCsv.Header;
    public string ScanParameter { get; set; } = "length";
    public double ScanStart { get; set; } = 3;
    public double ScanEnd { get; set; } = 4;
    public int ScanCount { get; set; } = 5;
    public string ReviewTarget { get; set; } = "";
    public string ReviewAction { get; set; } = "";
    public string ReviewOwner { get; set; } = "";
    private Action? pending;
    private Project? undo;

    public Mode? SelectedMode => Result?.Modes.FirstOrDefault(x => x.Key == SelectedModeKey) ?? Result?.Modes.FirstOrDefault();
    public EarPoint? SelectedSeat => Current?.Seats.FirstOrDefault(x => x.Id == SelectedSeatId);
    public Emitter? SelectedSource => Current?.Sources.FirstOrDefault(x => x.Id == SelectedSourceId);
    public Snapshot? Baseline => Current?.Baselines.FirstOrDefault(x => x.Id == BaselineId);
    public bool IsShape => Quantity == "shape";
    public string SaveDirectory => store.DirectoryPath;
    public string SourceLabel => Current is null ? "尚无项目" : Current.IsDemo ? "模拟示例 · 非实测" : "人工模型 · 待验证";
    public bool GeometryValid => Current is not null &&
        new[] { Current.Model.Length, Current.Model.Width, Current.Model.Height }
            .All(value => double.IsFinite(value) && value >= 0.5 && value <= 10);
    public bool CanUndo => undo is not null;
    public IEnumerable<Mode> VisibleModes => Result?.Modes.Where(x => ModeFilter == "全部" || x.Kind == ModeFilter).Take(100) ?? [];
    public List<Contribution> Contributions => Current is not null && Result is not null && SelectedSeat is not null ?
        Engine.Contributions(Current, Result, SelectedSeat, CursorFrequency).Take(12).ToList() : [];

    public FieldResult? Field()
    {
        if (Current is null || Result is null)
            return null;
        return Engine.Field(Current, Result, Plane, Slice, IsShape, SelectedMode, CursorFrequency);
    }

    public void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
    }

    public void Edited()
    {
        if (Current is null)
            return;
        Dirty = true;
        Current.Modified = DateTimeOffset.UtcNow;
        Invalidate();
        Status = "参数已改变，旧结果已清除。请点击“计算声场”。";
        Notify();
    }

    private void Invalidate()
    {
        Result = null;
        Difference = null;
        Scan = null;
        Csv = null;
        Truncation = null;
        DownloadUrl = "";
        Issues = [];
    }

    private void Journal(string action, string detail)
    {
        if (Current is null)
            return;
        Current.Journal.Add(new() { Action = action, Detail = detail });
        if (Current.Journal.Count > 500)
            Current.Journal.RemoveRange(0, Current.Journal.Count - 500);
        Current.Modified = DateTimeOffset.UtcNow;
        Dirty = true;
    }

    private void Remember()
    {
        if (Current is not null)
            undo = ProjectCodec.Clone(Current);
    }

    public void Undo()
    {
        if (undo is null)
            return;
        var previous = undo;
        undo = Current is null ? null : ProjectCodec.Clone(Current);
        Install(previous);
        Dirty = true;
        Status = "已恢复上一次结构操作前的项目；数值字段修改不在撤销范围。";
        Notify();
    }

    public void Request(string message, Action action)
    {
        Confirmation = message;
        pending = action;
        Notify();
    }

    public void Confirm()
    {
        var action = pending;
        pending = null;
        Confirmation = "";
        action?.Invoke();
        Notify();
    }

    public void Cancel()
    {
        pending = null;
        Confirmation = "";
        Notify();
    }

    private void Replace(Action action)
    {
        if (Dirty)
            Request("当前有未保存更改。继续将替换当前工作区，请先保存或确认放弃。", action);
        else
            action();
    }

    public void NewProject()
    {
        Replace(() =>
        {
            Install(new());
            Dirty = true;
            FileOpen = false;
            SettingsOpen = true;
            Status = "已建立空白项目。填写来源与模型，再添加声源和耳点。";
        });
    }

    public void LoadDemo()
    {
        Replace(() =>
        {
            Install(Demo.Create());
            Dirty = true;
            FileOpen = false;
            SettingsOpen = false;
            Calculate();
            Status = "已载入独立模拟示例；所有结果均非实测。";
        });
    }

    private void Install(Project project)
    {
        Current = project;
        Invalidate();
        SelectedSeatId = project.Seats.FirstOrDefault()?.Id ?? Guid.Empty;
        SelectedSourceId = project.Sources.FirstOrDefault()?.Id ?? Guid.Empty;
        BaselineId = project.Baselines.FirstOrDefault()?.Id ?? Guid.Empty;
        SelectedModeKey = "";
        CursorFrequency = project.Model.MinimumFrequency;
        CsvText = PointCsv.Header;
        Dirty = false;
        Notify();
    }

    public void Duplicate()
    {
        if (Current is null)
            return;
        var copy = ProjectCodec.Clone(Current);
        copy.Id = Guid.NewGuid();
        copy.Name = copy.Name.Length > 70 ? copy.Name[..70] + " 副本" : copy.Name + " 副本";
        copy.Created = DateTimeOffset.UtcNow;
        Install(copy);
        Journal("复制项目", "生成新项目标识；保留模型与点位标识以支持比较");
        Status = "副本已建立，保存后生成独立本机项目。";
        Notify();
    }

    public void RefreshLibrary()
    {
        Safe(() => Library = store.List());
    }

    public void Save()
    {
        if (Current is null)
            return;
        Safe(() =>
        {
            var problems = Validation.Check(Current, false);
            if (problems.Count > 0)
            {
                Issues = problems;
                Status = "保存失败，请修正列出的字段。";
                return;
            }
            Journal("保存项目", "写入本机项目目录，包含模型、点位、基准与复核清单");
            store.Save(Current);
            Dirty = false;
            Library = store.List();
            Status = "保存成功：" + Path.Combine(store.DirectoryPath, Current.Id + Software.Extension);
        });
    }

    public void Open(Guid id)
    {
        Replace(() => Safe(() =>
        {
            var project = store.Open(id);
            Install(project);
            FileOpen = false;
            Status = "已打开本机项目；请计算声场以重建结果。";
        }));
    }

    public void DeleteStored(Guid id)
    {
        Request("确认删除此本机保存文件？当前工作区仍可另行保存，删除文件无法自动恢复。", () => Safe(() =>
        {
            store.Delete(id);
            Library = store.List();
            Status = "本机项目文件已删除。";
        }));
    }

    public void Import(byte[] bytes)
    {
        Safe(() =>
        {
            var project = ProjectCodec.Decode(bytes);
            Replace(() =>
            {
                Install(project);
                Dirty = true;
                FileOpen = false;
                Status = "项目文件预检通过并载入；尚未写入本机目录。";
            });
        });
    }

    public void Calculate()
    {
        if (Current is null)
            return;
        Safe(() =>
        {
            Issues = Validation.Check(Current);
            if (Issues.Count > 0)
            {
                Result = null;
                Status = "计算被阻止，请按字段提示修正输入。";
                return;
            }
            Result = Engine.Calculate(Current);
            Difference = null;
            Scan = null;
            SelectedModeKey = Result.Modes.FirstOrDefault()?.Key ?? "";
            CursorFrequency = Math.Clamp(SelectedMode?.Frequency ?? Current.Model.MinimumFrequency,
                Current.Model.MinimumFrequency, Current.Model.MaximumFrequency);
            Journal("计算声场", $"{Result.Modes.Count}个非零模态；{Result.Frequencies.Count}个频点；{Current.Seats.Count}个耳点");
            Status = Result.Reference < 1e-12 ? "已计算；总响应近零，请检查源幅值、相位抵消及节点耦合。" :
                "计算完成。点击模态行查看振型，点击曲线区域选择激励频率。";
        });
    }

    public void SelectMode(Mode mode)
    {
        SelectedModeKey = mode.Key;
        Quantity = "shape";
        if (Current is not null)
            CursorFrequency = Math.Clamp(mode.Frequency, Current.Model.MinimumFrequency, Current.Model.MaximumFrequency);
        Notify();
    }

    public void SelectFrequency(double frequency)
    {
        if (Current is null)
            return;
        CursorFrequency = Math.Clamp(frequency, Current.Model.MinimumFrequency, Current.Model.MaximumFrequency);
        Quantity = "response";
        Notify();
    }

    public void AddSource()
    {
        if (Current is null || Current.Sources.Count >= 8)
            return;
        Remember();
        var source = new Emitter { Name = "声源" + (Current.Sources.Count + 1), Point = new(Current.Model.Length * 0.1, Current.Model.Width / 2, Current.Model.Height / 3) };
        Current.Sources.Add(source);
        SelectedSourceId = source.Id;
        Edited();
        Journal("添加声源", source.Name);
    }

    public void AddSeat()
    {
        if (Current is null || Current.Seats.Count >= 12)
            return;
        Remember();
        var seat = new EarPoint { Name = "耳点" + (Current.Seats.Count + 1), Point = new(Current.Model.Length / 2, Current.Model.Width / 2, Current.Model.Height * 0.7) };
        Current.Seats.Add(seat);
        SelectedSeatId = seat.Id;
        Edited();
        Journal("添加耳点", seat.Name);
    }

    public void RemoveSource(Guid id)
    {
        Request("确认移除此声源？相关计算结果将清除。", () =>
        {
            if (Current is null)
                return;
            Remember();
            Current.Sources.RemoveAll(x => x.Id == id);
            SelectedSourceId = Current.Sources.FirstOrDefault()?.Id ?? Guid.Empty;
            Edited();
            Journal("移除声源", id.ToString());
        });
    }

    public void RemoveSeat(Guid id)
    {
        Request("确认移除此耳点？相关计算结果将清除，基准快照保留原始点位。", () =>
        {
            if (Current is null)
                return;
            Remember();
            Current.Seats.RemoveAll(x => x.Id == id);
            SelectedSeatId = Current.Seats.FirstOrDefault()?.Id ?? Guid.Empty;
            Edited();
            Journal("移除耳点", id.ToString());
        });
    }

    public void PlacePoint(double u, double v)
    {
        if (Current is null || PointAction == "select")
            return;
        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var point = Engine.PlanePoint(Current.Model, Plane, u, v, Slice);
        Remember();
        if (PointAction == "source" && SelectedSource is not null)
            SelectedSource.Point = point;
        else if (PointAction == "seat" && SelectedSeat is not null)
            SelectedSeat.Point = point;
        else
        {
            Status = "请先添加并选中对应点位。";
            Notify();
            return;
        }
        Edited();
        Journal("画布放置点位", $"{Plane}剖面；x={point.X:F3},y={point.Y:F3},z={point.Z:F3}m");
    }

    public void Capture()
    {
        if (Current is null)
            return;
        Safe(() =>
        {
            if (Result is null)
                throw new ArgumentException("先计算当前模型，再保存基准");
            if (Current.Baselines.Count >= 12 || string.IsNullOrWhiteSpace(BaselineName) || BaselineName.Length > 80)
                throw new ArgumentException("基准名为1至80字符，最多12个基准");
            var snapshot = Demo.Capture(Current, BaselineName);
            Current.Baselines.Add(snapshot);
            BaselineId = snapshot.Id;
            Journal("保存基准", snapshot.Name);
            Status = "基准已冻结，修改当前模型不会改变基准。请保存项目持久化基准。";
        });
    }

    public void RestoreBaseline()
    {
        if (Current is null || Baseline is null)
            return;
        Request("用所选基准覆盖当前模型、声源与耳点？项目身份与其他基准保留。", () =>
        {
            Remember();
            Current.Model = ProjectCodec.Clone(Baseline.Model);
            Current.Sources = ProjectCodec.Clone(Baseline.Sources);
            Current.Seats = ProjectCodec.Clone(Baseline.Seats);
            Current.IsDemo = Baseline.IsDemo;
            Current.DataSource = Baseline.DataSource;
            Edited();
            SelectedSeatId = Current.Seats.FirstOrDefault()?.Id ?? Guid.Empty;
            SelectedSourceId = Current.Sources.FirstOrDefault()?.Id ?? Guid.Empty;
            Journal("恢复基准", Baseline.Name);
        });
    }

    public void DeleteBaseline()
    {
        if (Current is null || Baseline is null)
            return;
        Request("确认删除所选基准？", () =>
        {
            Remember();
            var name = Baseline.Name;
            Current.Baselines.RemoveAll(x => x.Id == BaselineId);
            BaselineId = Current.Baselines.FirstOrDefault()?.Id ?? Guid.Empty;
            Difference = null;
            Journal("删除基准", name);
        });
    }

    public async Task CheckTruncation()
    {
        if (Current is null || Result is null || Busy)
            return;
        Busy = true;
        Status = "正在增加截断复核…";
        Notify();
        var project = ProjectCodec.Clone(Current);
        var original = Result;
        var modified = Current.Modified;
        try
        {
            var study = await Task.Run(() => Diagnostics.Extend(project, original));
            if (Current?.Id != project.Id || Current.Modified != modified)
            {
                Status = "工作对象已变化，丢弃旧条件的截断复核。";
                return;
            }
            Truncation = study;
            Status = "截断敏感性复核完成，原项目参数保持。";
        }
        catch (ArgumentException error)
        {
            Status = error.Message;
            Truncation = null;
        }
        finally
        {
            Busy = false;
            Notify();
        }
    }

    public void ClearComparison()
    {
        Difference = null;
        Notify();
    }

    public void Compare()
    {
        Safe(() =>
        {
            Difference = null;
            if (Current is null || Result is null || Baseline is null)
                throw new ArgumentException("请先计算并选择一个基准");
            Difference = Comparison.Compare(Current, Result, Baseline);
            Status = "比较完成；表格比值使用同量纲原始幅值，曲线使用共同参考。";
        });
    }

    public async Task RunScan()
    {
        if (Current is null || Busy)
            return;
        Busy = true;
        Status = "正在扫描，请稍候。";
        Notify();
        await Task.Delay(30);
        var copy = ProjectCodec.Clone(Current);
        var modified = Current.Modified;
        var settings = new ScanSettings(ScanParameter, ScanStart, ScanEnd, ScanCount, SelectedSeatId);
        try
        {
            var scan = await Task.Run(() => ParameterScan.Run(copy, settings));
            if (Current?.Id != copy.Id || Current.Modified != modified)
            {
                Status = "工作对象已变化，丢弃旧条件的扫描结果。";
                return;
            }
            Scan = scan;
            Status = "扫描完成；" + Scan.Rows.Count(x => x.Error.Length == 0) + "项有效。错误行已保留原因。";
        }
        catch (ArgumentException error)
        {
            Scan = null;
            Status = error.Message;
        }
        finally
        {
            Busy = false;
            Notify();
        }
    }

    public void PreviewCsv()
    {
        if (Current is null)
            return;
        Csv = PointCsv.Preview(CsvText, Current.Model);
        Status = Csv.CanApply ? "预检通过。确认应用会替换所有当前声源和耳点，并重新生成点位标识。" : "CSV预检失败，项目未改变。";
        Notify();
    }

    public void ApplyCsv()
    {
        if (Current is null || Csv is null || !Csv.CanApply)
            return;
        Request("CSV将替换所有声源与耳点，重新生成标识，旧基准耳点将无法按标识匹配。继续？", () =>
        {
            Remember();
            PointCsv.Apply(Current, Csv);
            SelectedSeatId = Current.Seats.FirstOrDefault()?.Id ?? Guid.Empty;
            SelectedSourceId = Current.Sources.FirstOrDefault()?.Id ?? Guid.Empty;
            Edited();
            Journal("点位CSV替换", "已通过预检；新点位标识");
        });
    }

    public void AddReview()
    {
        if (Current is null)
            return;
        Safe(() =>
        {
            if (string.IsNullOrWhiteSpace(ReviewTarget) || string.IsNullOrWhiteSpace(ReviewAction))
                throw new ArgumentException("填写复核对象和具体步骤");
            if (ReviewTarget.Length > 200 || ReviewAction.Length > 1000 || ReviewOwner.Length > 80 || Current.Reviews.Count >= 100)
                throw new ArgumentException("复核对象最多200字、步骤1000字、负责人80字、总计100项");
            Current.Reviews.Add(new() { Target = ReviewTarget, Action = ReviewAction, Owner = ReviewOwner });
            Journal("添加复核项", ReviewTarget);
            ReviewTarget = "";
            ReviewAction = "";
            Status = "复核项已加入项目。";
        });
    }

    public void ReviewEdited()
    {
        Journal("更新复核记录", "人工填写结果；未自动判定");
        Notify();
    }

    public void RemoveReview(Guid id)
    {
        Request("确认删除此复核记录？", () =>
        {
            Remember();
            Current?.Reviews.RemoveAll(x => x.Id == id);
            Journal("删除复核项", id.ToString());
        });
    }

    public void Export(string kind)
    {
        if (Current is null)
            return;
        Safe(() =>
        {
            if (kind == "project")
            {
                DownloadName = "座舱项目" + Software.Extension;
                DownloadUrl = vault.Add(DownloadName, "application/json", ProjectCodec.Encode(Current));
            }
            else
            {
                var text = kind switch
                {
                    "points" => PointCsv.Export(Current),
                    "reviews" => Exports.ReviewsCsv(Current),
                    "modes" when Result is not null => Exports.ModesCsv(Current, Result),
                    "responses" when Result is not null => Exports.ResponsesCsv(Current, Result),
                    "contributions" when Result is not null && SelectedSeat is not null => Exports.ContributionsCsv(Current, Result, SelectedSeat, CursorFrequency),
                    "comparison" when Difference is not null && Baseline is not null => Exports.ComparisonCsv(Current, Baseline, Difference),
                    "scan" when Scan is not null => Exports.ScanCsv(Current, Scan),
                    "report" when Result is not null => Exports.Html(Current, Result, Difference, Scan),
                    _ => throw new ArgumentException("此成果尚无有效结果，请先计算对应内容")
                };
                DownloadName = "座舱研究-" + kind + (kind == "report" ? ".html" : ".csv");
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
                DownloadUrl = vault.Add(DownloadName, kind == "report" ? "text/html; charset=utf-8" : "text/csv; charset=utf-8", bytes);
            }
            Status = "导出已准备：" + DownloadName + "。点击下载，保存位置由浏览器下载设置决定；链接20分钟有效。";
        });
    }

    public void Error(string message)
    {
        Status = message;
        Notify();
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Status = "操作失败：" + error.Message;
        }
        Notify();
    }
}
