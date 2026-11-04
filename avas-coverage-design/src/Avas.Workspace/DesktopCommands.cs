using System.ComponentModel;
using System.Windows.Input;
using Avas.Core.Analysis;
using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.Workspace;

public interface IDesktopServices
{
    bool Confirm(string title, string message);
    byte[]? ReadFile(string kind);
    string LastReadName { get; }
    string WriteExport(byte[] bytes, string suggestedName);
    string WriteTemporaryAudio(byte[] bytes);
    void PlayAudio(string path);
    void StopAudio();
}

public sealed class ActionCommand : ICommand
{
    private readonly Func<object?, Task> action;
    private readonly Func<bool> available;
    public event EventHandler? CanExecuteChanged;

    public ActionCommand(Func<object?, Task> action, Func<bool> available)
    {
        this.action = action;
        this.available = available;
    }

    public bool CanExecute(object? parameter) => available();
    public async void Execute(object? parameter)
    {
        if (available())
            await action(parameter);
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class WorkspaceViewModel
{
    private readonly Dictionary<string, ActionCommand> commands = [];
    public IReadOnlyDictionary<string, ActionCommand> Commands => commands;
    public string[] Sections { get; } = ["项目", "扬声器", "背景与网格", "观察点", "方案比较", "扫描与剖面", "音频试听", "成果与复核"];
    public Origin[] Origins { get; } = Enum.GetValues<Origin>();
    public ScanParameter[] ScanParameters { get; } = Enum.GetValues<ScanParameter>();
    public int[] MetricBands { get; } = [0, .. Software.Frequencies];
    public string SelectedSection
    {
        get => Tab;
        set
        {
            if (value == Tab)
                return;
            Execute(() =>
            {
                RequireApplied();
                Tab = value;
            });
        }
    }
    public Speaker? SelectedSpeaker
    {
        get => SpeakerDraft;
        set
        {
            if (value is null || value.Id == SelectedSpeakerId)
                return;
            Execute(() =>
            {
                RequireApplied();
                SelectSpeaker(value.Id);
            });
        }
    }
    public Listener? SelectedListener
    {
        get => ListenerDraft;
        set
        {
            if (value is null || value.Id == SelectedListenerId)
                return;
            Execute(() =>
            {
                RequireApplied();
                SelectListener(value.Id);
            });
        }
    }
    public StoredProject? SelectedStored { get; set; }
    public Snapshot? SelectedSnapshot { get; set; }
    public Guid? BaselineKey
    {
        get => Guid.TryParse(BaselineId, out var id) ? id : null;
        set { if (value is { } id) BaselineId = id.ToString(); }
    }
    public Guid? TargetKey
    {
        get => Guid.TryParse(TargetId, out var id) ? id : null;
        set { if (value is { } id) TargetId = id.ToString(); }
    }
    public ScanRow? SelectedScan { get; set; }
    public double RingRadius { get; set; } = 5;
    public int RingCount { get; set; } = 8;
    public string InputState => BindingErrorCount > 0 ? $"存在 {BindingErrorCount} 个格式错误" : HasDraft ? "字段草稿未应用" : "字段已应用";
    public string ResultState => Stale || HasDraft ? "结果待计算 / 已过期" : "结果与当前输入一致";
    public string SaveState => Dirty || HasDraft ? "项目尚未保存" : "项目已保存 / 空白初始状态";
    public string SourceState => ProjectCodec.SourceSummary(Project);
    public bool IsReady => !Busy;
    public string AudioAssetSummary => CurrentAudio() is { } asset
        ? $"{asset.Name} | {asset.Rate} Hz | {asset.Channels} 声道 | {asset.Duration:F3} s | SHA256 {asset.Sha256}"
        : "当前对象没有内嵌音频；可导入 PCM16 WAV。";
    public double? DigitalRmsDbfs => AudioSpectrum is { FullRms: > 0 } spectrum ? 20 * Math.Log10(spectrum.FullRms) : null;
    public IEnumerable<DigitalBandRow> DigitalBands => AudioSpectrum is not { } spectrum ? []
        : Software.Frequencies.Select((hz, i) => new DigitalBandRow(hz, spectrum.Levels[i]));
    public string MetricLabel => Acoustics.Metric(Project.Settings);
    public Calculation? CurrentResult => Stale || HasDraft ? null : Result;
    public PointResult? CurrentPoint => CurrentResult?.Listeners.SingleOrDefault(x => x.ListenerId == SelectedListenerId);
    public IEnumerable<FrequencyRow> PointBands => CurrentPoint is not { } point ? []
        : Software.Frequencies.Select((hz, i) => new FrequencyRow(hz, point.Bands[i], Project.Settings.Background.Single(x => x.Hz == hz).Level));
    public IEnumerable<SourceRow> SourceRows => CurrentPoint is not { } point ? []
        : point.Contributions.Select(x => new SourceRow(x.Name, x.Distance, x.Loss, Acoustics.Select(x.Bands, Project.Settings), x.Origin));

    public void DisposeServices()
    {
        files.StopAudio();
        if (files is IDisposable disposable)
            disposable.Dispose();
    }

    private void InitializeCommands()
    {
        Add("New", _ => ReplaceSession(false));
        Add("Demo", _ => ReplaceSession(true));
        Add("OpenFile", _ =>
        {
            if (!ConfirmReplacement())
                return;
            var bytes = files.ReadFile("project");
            if (bytes is not null)
                ImportProject(bytes);
        });
        Add("OpenStored", _ => OpenStored(false));
        Add("OpenBackup", _ => OpenStored(true));
        Add("RefreshLibrary", _ => RefreshLibrary());
        Add("Save", _ => Save());
        Add("Apply", _ => ApplyDraft());
        Add("Discard", _ => DiscardDraft());
        Add("Undo", _ => Undo());
        Add("Redo", _ => Redo());
        Add("CopyProject", _ => CopyProject());
        Add("Archive", _ =>
        {
            if (files.Confirm("归档状态", Project.Archived ? "解除项目归档，允许编辑？" : "归档后只读，仍可计算与导出。继续？"))
                ToggleArchive();
        });
        Add("AddSpeaker", _ => AddSpeaker());
        Add("CopySpeaker", _ => DuplicateSpeaker());
        Add("DeleteSpeaker", _ =>
        {
            if (SpeakerDraft is not null && files.Confirm("删除扬声器", $"删除 {SpeakerDraft.Name}？历史快照独立保留。"))
                RemoveSpeaker();
        });
        Add("AddListener", _ => AddListener());
        Add("DeleteListener", _ =>
        {
            if (ListenerDraft is not null && files.Confirm("删除观察点", $"删除 {ListenerDraft.Name}？"))
                RemoveListener();
        });
        Add("Ring", _ => AddRing(RingRadius, RingCount));
        Add("Capture", _ => CaptureSnapshot());
        Add("Restore", _ =>
        {
            var snapshot = SelectedSnapshot ?? throw new ArgumentException("请先在列表中选择快照");
            if (files.Confirm("恢复快照", "替换当前输入为所选快照？可通过撤销恢复。"))
                RestoreSnapshot(snapshot.Id);
        });
        Add("DeleteSnapshot", _ =>
        {
            var snapshot = SelectedSnapshot ?? throw new ArgumentException("请先选择快照");
            if (files.Confirm("删除快照", $"删除 {snapshot.Name}？"))
                DeleteSnapshot(snapshot.Id);
        });
        Add("Compare", _ => Compare());
        Add("ApplyScan", _ => ApplyScan((SelectedScan ?? throw new ArgumentException("请先选择扫描结果行")).Value));
        Add("SpectrumPreview", _ => PreviewCsv());
        Add("SpectrumApply", _ => ApplyCsv());
        Add("ListenerPreview", _ => PreviewListeners());
        Add("ListenerApply", _ =>
        {
            if (!ReplaceListeners || files.Confirm("替换点集", "用预检结果整体替换观察点？历史快照不变，可撤销。"))
                ApplyListeners();
        });
        Add("ReadSpectrum", _ => ReadText(false));
        Add("ReadListeners", _ => ReadText(true));
        Add("ImportAudio", _ =>
        {
            RequireApplied();
            RequireEditable();
            var bytes = files.ReadFile("wave");
            if (bytes is not null)
                ImportAudio(files.LastReadName, bytes);
        });
        Add("AnalyzeAudio", _ => AnalyzeAudio());
        Add("MapAudio", _ => ApplyAudioSpectrum());
        Add("RemoveAudio", _ =>
        {
            if (files.Confirm("移除音频", "移除当前对象的内嵌数字音频？参考声压级不会改变。"))
                RemoveAudio();
        });
        Add("Play", _ =>
        {
            RequireApplied();
            if (Audition is null || Audition.Hash != ProjectCodec.InputHash(Project) || AudioUrl is null)
                throw new ArgumentException("请先制作当前输入的相对试听");
            files.PlayAudio(AudioUrl);
            Status = "正在播放相对试听；实际设备音量未标定。";
        });
        Add("Stop", _ => { files.StopAudio(); Status = "已停止试听。"; });
        Add("ExportAudio", _ =>
        {
            RequireApplied();
            if (Audition is null || Audition.Hash != ProjectCodec.InputHash(Project))
                throw new ArgumentException("请先制作当前输入的试听");
            LastDownload = files.WriteExport(Audition.Wave, SafeName(Project.Name) + "-相对试听.wav");
            Status = "已保存试听：" + LastDownload;
        });
        Add("Export", parameter =>
        {
            switch (parameter?.ToString())
            {
                case "transect": ExportTransect(); break;
                case "listeners": ExportListeners(false); break;
                case "listener-template": ExportListeners(true); break;
                default: Export(parameter?.ToString() ?? "report"); break;
            }
        });
        AddAsync("Calculate", CalculateAsync);
        AddAsync("Scan", RunScanAsync);
        AddAsync("Transect", RunTransectAsync);
        AddAsync("Audition", RenderAuditionAsync);
    }

    private void ReadText(bool listeners)
    {
        RequireApplied();
        var bytes = files.ReadFile("csv");
        if (bytes is null)
            return;
        if (bytes.Length > 1024 * 1024)
            throw new ArgumentException("CSV超过1MiB，请缩小输入文件");
        var text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        if (listeners)
            ListenerText = text;
        else
            CsvText = text;
        Status = "已读取UTF-8 CSV文本，尚未应用；请预检后确认。";
    }

    private void Add(string name, Action<object?> action)
    {
        commands.Add(name, new ActionCommand(p =>
        {
            Execute(() => action(p));
            return Task.CompletedTask;
        }, () => !Busy));
    }

    private void AddAsync(string name, Func<Task> action)
    {
        commands.Add(name, new ActionCommand(async _ =>
        {
            try
            {
                RequireValidBindings();
                await action();
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or OverflowException)
            {
                Failure(error.Message);
            }
        }, () => !Busy));
    }

    private async Task RunWorkAsync<T>(Func<T> compute, Action<T> accept)
    {
        if (Busy)
            return;
        Busy = true;
        Status = "正在处理，请等待…";
        Errors = [];
        Notify();
        try
        {
            var result = await Task.Run(compute);
            accept(result);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or OverflowException)
        {
            Failure(error.Message);
        }
        finally
        {
            Busy = false;
            Notify();
        }
    }

    private void RequireValidBindings()
    {
        if (BindingErrorCount > 0)
            throw new ArgumentException("存在格式错误，请先修正红框字段；数字不能填入文字或留空。");
    }

    public bool CanClose()
    {
        if (Busy)
        {
            Status = "任务进行中，请等待结束再关闭。";
            Notify();
            return false;
        }
        return !(Dirty || HasDraft || BindingErrorCount > 0) || files.Confirm("未保存内容", "存在未保存内容。放弃并关闭？如需保留，请取消，应用编辑后保存到项目库。");
    }

    private bool ConfirmReplacement()
    {
        return !(Dirty || HasDraft || BindingErrorCount > 0) || files.Confirm("替换当前项目", "存在未保存内容。放弃并替换当前项目？如需保留，请先取消并保存。");
    }

    private void ReplaceSession(bool demo)
    {
        if (ConfirmReplacement())
        {
            BindingErrorCount = 0;
            New(demo);
        }
    }

    private void OpenStored(bool backup)
    {
        var item = SelectedStored ?? throw new ArgumentException("请先选择项目库条目");
        if (!Guid.TryParse(item.Id, out var id))
            throw new ArgumentException("项目库条目标识无效，请导入完整备份文件");
        if (ConfirmReplacement())
        {
            BindingErrorCount = 0;
            Open(id, backup);
        }
    }
}

public sealed record FrequencyRow(int Hz, double Signal, double Background)
{
    public double Margin => Signal - Background;
}
public sealed record SourceRow(string Name, double Distance, double Loss, double Signal, string Origin);

public sealed record DigitalBandRow(int Hz, double Dbfs);
