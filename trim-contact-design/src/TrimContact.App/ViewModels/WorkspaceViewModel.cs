using System.ComponentModel;
using System.Text.Json;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.App.ViewModels;

public sealed partial class WorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProjectStore store;
    private readonly DownloadVault downloads;
    private CancellationTokenSource? calculation;
    public ProjectSession Session { get; } = new();
    public DesignProject Project => Session.Current;
    public DesignProject Draft { get; private set; }
    public AnalysisReport? Report { get; private set; }
    public IReadOnlyList<SweepRow> SweepRows { get; private set; } = [];
    public IReadOnlyList<StoredProject> Library { get; private set; } = [];
    public string Tab { get; set; } = "项目";
    public string Status { get; private set; } = "从空白项目开始，或明确载入模拟演示。";
    public bool IsError { get; private set; }
    public bool Busy { get; private set; }
    public int Progress { get; private set; }
    public string? DownloadUrl { get; private set; }
    public string? DownloadName { get; private set; }
    public string PendingTitle { get; private set; } = "";
    private Action? pending;
    public Guid? VariantId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? ClipId { get; set; }
    public Guid? LoadId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ResultVariantId { get; set; }
    public Guid? ResultLoadId { get; set; }
    public Guid? ResultPointId { get; set; }
    public DesignVariant? Variant => Draft.Variants.FirstOrDefault(v => v.Id == VariantId);
    public ContactPoint? Contact => Variant?.Contacts.FirstOrDefault(c => c.Id == ContactId);
    public Clip? Clip => Variant?.Clips.FirstOrDefault(c => c.Id == ClipId);
    public LoadCase? Load => Draft.Loads.FirstOrDefault(l => l.Id == LoadId);
    public VerificationTask? Task => Draft.Tasks.FirstOrDefault(t => t.Id == TaskId);
    public VariantResult? Result => Report?.Variants.FirstOrDefault(v => v.VariantId == ResultVariantId)
        ?? Report?.Variants.FirstOrDefault();
    public TraceResult? Trace => Result?.NominalTraces.FirstOrDefault(t => t.LoadId == ResultLoadId)
        ?? Result?.NominalTraces.FirstOrDefault();
    public PointEnvelope? ResultPoint => Result?.Contacts.Concat(Result.Clips).FirstOrDefault(c => c.PointId == ResultPointId)
        ?? Result?.Contacts.FirstOrDefault() ?? Result?.Clips.FirstOrDefault();
    private static string State(DesignProject p) => JsonSerializer.Serialize(p, ProjectCodec.Options);
    public bool DraftChanged => State(Draft) != State(Project);
    public bool Dirty => Session.Dirty || DraftChanged;
    public bool CurrentResults => Report is not null && !DraftChanged && Report.InputHash == ProjectCodec.InputHash(Project);
    public string SweepParameter { get; set; } = "预紧量mm";
    public double SweepFrom { get; set; } = 0;
    public double SweepTo { get; set; } = 0.5;
    public int SweepSteps { get; set; } = 6;
    private string sweepInputHash = "";
    private string sweepParameterRun = "";
    public bool CurrentSweep => SweepRows.Count > 0 && !DraftChanged && sweepInputHash == ProjectCodec.InputHash(Project);
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceViewModel(ProjectStore store, DownloadVault downloads)
    {
        this.store = store;
        this.downloads = downloads;
        Draft = ProjectCodec.Clone(Project);
        RefreshLibrary();
    }

    public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void Message(string message, bool error = false)
    {
        Status = message;
        IsError = error;
        Notify();
    }

    public void Execute(Action action)
    {
        if (Busy)
            return;
        try
        {
            action();
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Message(error.Message, true);
        }
        Notify();
    }

    private void SyncDraft()
    {
        Draft = ProjectCodec.Clone(Project);
        NormalizeSelection();
        DownloadUrl = null;
    }

    private void NormalizeSelection()
    {
        if (!Draft.Variants.Any(v => v.Id == VariantId))
            VariantId = Draft.Variants.FirstOrDefault()?.Id;
        if (Variant is null || !Variant.Contacts.Any(c => c.Id == ContactId))
            ContactId = Variant?.Contacts.FirstOrDefault()?.Id;
        if (Variant is null || !Variant.Clips.Any(c => c.Id == ClipId))
            ClipId = Variant?.Clips.FirstOrDefault()?.Id;
        if (!Draft.Loads.Any(l => l.Id == LoadId))
            LoadId = Draft.Loads.FirstOrDefault()?.Id;
        if (!Draft.Tasks.Any(t => t.Id == TaskId))
            TaskId = Draft.Tasks.FirstOrDefault()?.Id;
    }

    public void SelectVariant(Guid id)
    {
        VariantId = id;
        ContactId = Variant?.Contacts.FirstOrDefault()?.Id;
        ClipId = Variant?.Clips.FirstOrDefault()?.Id;
        Notify();
    }

    public void ApplyDraft()
    {
        if (!DraftChanged)
        {
            Message("编辑已应用；当前输入没有变化。");
            return;
        }
        ProjectValidation.Require(Draft);
        Session.Edit("应用项目、布置、载荷及验证任务编辑", p =>
        {
            var copy = ProjectCodec.Clone(Draft);
            p.Name = copy.Name;
            p.Revision = copy.Revision;
            p.Owner = copy.Owner;
            p.Goal = copy.Goal;
            p.Source = copy.Source;
            p.Origin = copy.Origin;
            p.Panel = copy.Panel;
            p.Settings = copy.Settings;
            p.Variants = copy.Variants;
            p.Loads = copy.Loads;
            p.Tasks = copy.Tasks;
        });
        SyncDraft();
        Message("编辑已应用；输入变化后需重新计算，保存后可重开。");
    }

    public void Request(string title, Action action)
    {
        if (Busy)
            return;
        PendingTitle = title;
        pending = action;
        Notify();
    }

    public void Confirm()
    {
        var action = pending;
        pending = null;
        PendingTitle = "";
        if (action is not null)
            Execute(action);
    }

    public void CancelPending()
    {
        pending = null;
        PendingTitle = "";
        Notify();
    }

    private void Replace(string title, Action action)
    {
        if (Dirty)
            Request(title + "；当前有未保存或未应用内容，确认丢弃？", action);
        else
            Execute(action);
    }

    public void NewProject() => Replace("新建空白项目", () =>
    {
        Session.Load(new DesignProject());
        Report = null;
        SweepRows = [];
        SyncDraft();
        Message("已建立空白项目；请定义轮廓、点位和载荷。");
    });

    public void Demo() => Replace("载入固定模拟演示", () =>
    {
        Session.Load(Demonstration.Create());
        Report = null;
        SweepRows = [];
        SyncDraft();
        Message("已载入模拟参数；非实测。可修改并复制比较方案。");
    });

    public void CopyProject() => Execute(() =>
    {
        ApplyDraft();
        Session.CopyProject();
        SyncDraft();
        Message("项目已复制并分配新ID，请保存。");
    });

    public void Archive() => Request("归档当前项目并保存；归档后只读，继续编辑需复制项目。", () =>
    {
        ApplyDraft();
        Session.Edit("归档项目", p => p.Archived = true);
        SyncDraft();
        Save();
    });

    public void Undo() => Execute(() =>
    {
        if (DraftChanged)
        {
            Request("撤销前丢弃未应用编辑？", () => { Session.Undo(); SyncDraft(); });
            return;
        }
        Session.Undo();
        SyncDraft();
        Message("已撤销最近一次应用；需要时重新计算。");
    });

    public void Redo() => Execute(() =>
    {
        if (DraftChanged)
            throw new InvalidOperationException("请先应用或撤销未应用编辑");
        Session.Redo();
        SyncDraft();
        Message("已重做。");
    });

    public void RefreshLibrary()
    {
        Library = store.List();
        Notify();
    }

    public void Open(Guid id, bool backup = false) => Replace(backup ? "载入上次覆盖前备份" : "打开磁盘项目", () =>
    {
        var opened = store.Open(id, backup);
        Session.Load(opened.Project, opened.Hash, store.FilePath(id));
        Report = null;
        SweepRows = [];
        SyncDraft();
        Message(backup ? "已载入备份；确认输入后保存可恢复主文件。" : "已从磁盘实际重开项目：" + opened.Path);
    });

    public void Save() => Execute(() =>
    {
        ApplyDraft();
        var result = store.Save(Project, Session.DiskHash);
        Session.Saved(result);
        RefreshLibrary();
        Message("已保存到：" + result.Path);
    });

    public void ImportProject(string text)
    {
        Execute(() =>
        {
            var imported = ProjectCodec.Parse(text);
            Replace("导入项目文件", () =>
            {
                Session.Load(imported);
                Report = null;
                SweepRows = [];
                SyncDraft();
                Message("项目已导入到工作副本；同ID文件不会直接覆盖，已有项目请先复制再保存。");
            });
        });
    }

    public async Task CalculateAsync()
    {
        if (Busy)
            return;
        try
        {
            ApplyDraft();
            ProjectValidation.Require(Project, true);
            var input = ProjectCodec.Clone(Project);
            Busy = true;
            Progress = 0;
            calculation = new CancellationTokenSource();
            Message("正在计算法向力平衡与切向库仑限幅……");
            var result = await System.Threading.Tasks.Task.Run(() => AnalysisEngine.Run(input, (percent, message) =>
            {
                Progress = percent;
                Status = message;
                Notify();
            }, calculation.Token));
            Report = result;
            ResultVariantId = result.Variants.First().VariantId;
            ResultLoadId = result.Variants.First().NominalTraces.First().LoadId;
            ResultPointId = null;
            Tab = "结果";
            Message("计算完成。存在分离或滑移是简化模型筛查，需要实物核查。");
        }
        catch (OperationCanceledException)
        {
            Message("计算已取消，未发布部分结果。");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Message(error.Message, true);
        }
        finally
        {
            Busy = false;
            calculation?.Dispose();
            calculation = null;
            Notify();
        }
    }

    public void CancelCalculation() => calculation?.Cancel();

    public async Task ScanAsync()
    {
        if (Busy)
            return;
        try
        {
            ApplyDraft();
            if (VariantId is null || ContactId is null)
                throw new InvalidOperationException("先在布置页选择一个方案和接触点");
            ProjectValidation.Require(Project, true);
            var input = ProjectCodec.Clone(Project);
            var variant = VariantId.Value;
            var contact = ContactId.Value;
            var parameter = SweepParameter;
            var from = SweepFrom;
            var to = SweepTo;
            var steps = SweepSteps;
            Busy = true;
            calculation = new CancellationTokenSource();
            Message("正在逐个参数值重新求解；扫描不会修改设计参数……");
            SweepRows = await System.Threading.Tasks.Task.Run(() => AnalysisEngine.Sweep(input, variant, contact,
                parameter, from, to, steps, calculation.Token));
            sweepInputHash = ProjectCodec.InputHash(Project);
            sweepParameterRun = parameter;
            Message("扫描完成，请结合卡扣受力和接触压缩选择候选参数，再回布置页手动应用。");
        }
        catch (OperationCanceledException)
        {
            Message("扫描已取消。");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            Message(error.Message, true);
        }
        finally
        {
            Busy = false;
            calculation?.Dispose();
            calculation = null;
            Notify();
        }
    }

    public void GenerateTasks() => Execute(() =>
    {
        ApplyDraft();
        if (!CurrentResults)
            throw new InvalidOperationException("先计算当前输入再生成验证清单");
        var count = 0;
        Session.Edit("从当前接触筛查生成验证任务", p => count = DesignOperations.GenerateTasks(p, Report!));
        SyncDraft();
        Message($"已增加{count}项验证任务；相同标题与点位不重复生成。判据须由工程负责人确认。");
    });

    public void Export(string kind) => Execute(() =>
    {
        ApplyDraft();
        var name = "接触设计-" + Project.Id.ToString("N")[..8];
        string text;
        string contentType;
        string extension;
        if (kind == "project")
        {
            text = ProjectCodec.Serialize(Project);
            contentType = "application/json; charset=utf-8";
            extension = ".trimproj";
        }
        else if (kind == "tasks")
        {
            text = ResultExport.TasksCsv(Project);
            contentType = "text/csv; charset=utf-8";
            extension = "-验证清单.csv";
        }
        else if (kind == "sweep")
        {
            if (!CurrentSweep)
                throw new InvalidOperationException("扫描已失效或尚未执行");
            text = ResultExport.SweepCsv(Project, sweepParameterRun, SweepRows);
            contentType = "text/csv; charset=utf-8";
            extension = "-参数扫描.csv";
        }
        else
        {
            if (!CurrentResults)
                throw new InvalidOperationException("结果不存在或已失效，请计算后导出");
            text = kind switch
            {
                "csv" => ResultExport.Csv(Project, Report!),
                "balance" => BalanceExport.Csv(Project, Report!),
                "trace" => ResultExport.TraceCsv(Project, Report!),
                "json" => ResultExport.Json(Project, Report!),
                "html" => ResultExport.Html(Project, Report!),
                _ => throw new InvalidOperationException("未知导出类型")
            };
            contentType = kind == "html" ? "text/html; charset=utf-8"
                : kind == "json" ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
            extension = kind == "balance" ? "-平衡核查.csv" : kind == "trace" ? "-逐相位.csv" : "." + kind;
        }
        DownloadName = name + extension;
        DownloadUrl = downloads.Add(text, contentType, DownloadName);
        Message("已生成下载文件：" + DownloadName + "；点击下载链接并在浏览器确认保存位置。");
    });

    public void Dispose()
    {
        calculation?.Cancel();
        calculation?.Dispose();
    }
}
