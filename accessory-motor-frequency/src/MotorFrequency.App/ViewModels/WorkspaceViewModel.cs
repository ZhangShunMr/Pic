using System.ComponentModel;
using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.App.ViewModels;

public sealed class WorkspaceViewModel : INotifyPropertyChanged
{
    private readonly ProjectStore store;
    private readonly IJSRuntime js;
    private readonly Stack<string> undo = new();
    private readonly Stack<string> redo = new();
    private string? revision;
    private string savedHash = "";
    private Action? pending;
    private string? pendingProjectText;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Project? Current { get; private set; }
    public MotorDraft Editor { get; private set; } = new();
    public string SelectedObject { get; private set; } = "rotor";
    public string SelectedCandidateId { get; private set; } = "electrical";
    public Guid? SelectedSpectrumId { get; set; }
    public Guid? SelectedScenarioId { get; set; }
    public string Message { get; private set; } = "新建空白项目，或从独立示例开始。";
    public bool IsError { get; private set; }
    public string Confirmation { get; private set; } = "";
    public string LastPath { get; private set; } = "尚未保存";
    public IReadOnlyList<ProjectSummary> Library { get; private set; } = [];
    public IReadOnlyList<string> LibraryWarnings { get; private set; } = [];
    public string ProjectName { get; set; } = "附件电机频率项目";
    public string Owner { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string OrderName { get; set; } = "";
    public string OrderValue { get; set; } = "2";
    public string OrderBasis { get; set; } = "";
    public string BandName { get; set; } = "";
    public string BandLower { get; set; } = "55";
    public string BandUpper { get; set; } = "65";
    public string BandBasis { get; set; } = "";
    public DataOrigin BandOrigin { get; set; }
    public string SpectrumName { get; set; } = "";
    public string SpectrumRpm { get; set; } = "1800";
    public string SpectrumUnit { get; set; } = "dB";
    public string SpectrumReference { get; set; } = "";
    public string SpectrumCondition { get; set; } = "";
    public DataOrigin SpectrumOrigin { get; set; }
    public string CsvText { get; set; } = "";
    public int CsvPickerVersion { get; private set; }
    public string CsvFileName { get; private set; } = "手动粘贴.csv";
    public CsvImportResult? Preview { get; private set; }
    public string ScenarioName { get; set; } = "";
    public string Tolerance { get; set; } = "3";
    public string PeakCount { get; set; } = "10";
    public string ReviewNotes { get; set; } = "";
    public bool HasDraftChanges
    {
        get
        {
            if (Current is null) return false;
            if (ProjectName != Current.Name || Owner != Current.Owner || Purpose != Current.Purpose)
            {
                return true;
            }
            if (Tolerance != Labels.Number(Current.Review.MatchToleranceHz)
                || PeakCount != Current.Review.MaximumPeaks.ToString()
                || ReviewNotes != Current.Review.Notes)
            {
                return true;
            }
            var applied = MotorDraft.From(Current.Motor);
            return Editor.Name != applied.Name
                || Editor.Topology != applied.Topology
                || Editor.PolePairs != applied.PolePairs
                || Editor.Slots != applied.Slots
                || Editor.Segments != applied.Segments
                || Editor.MinimumRpm != applied.MinimumRpm
                || Editor.MaximumRpm != applied.MaximumRpm
                || Editor.StepRpm != applied.StepRpm
                || Editor.InspectRpm != applied.InspectRpm
                || Editor.MaximumHz != applied.MaximumHz
                || Editor.IncludeSlotPass != applied.IncludeSlotPass
                || Editor.IncludeCogging != applied.IncludeCogging
                || Editor.Symmetric != applied.Symmetric
                || Editor.Assumptions != applied.Assumptions;
        }
    }

    public void DraftChanged()
    {
        Notify();
    }

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool Dirty => Current is not null && ProjectCodec.Hash(ProjectCodec.Serialize(Current)) != savedHash;
    public IReadOnlyList<Candidate> Candidates => Current is null ? [] : FrequencyEngine.Candidates(Current.Motor, Current.CustomOrders);
    public Candidate? SelectedCandidate => Candidates.FirstOrDefault(x => x.Id == SelectedCandidateId);
    public Spectrum? SelectedSpectrum => Current?.Spectra.FirstOrDefault(x => x.Id == SelectedSpectrumId);
    public Scenario? SelectedScenario => Current?.Scenarios.FirstOrDefault(x => x.Id == SelectedScenarioId);
    public IReadOnlyList<BandCrossing> Crossings => Current is null ? [] : FrequencyEngine.Crossings(Current);
    public IReadOnlyList<PeakMatch> PeakMatches => Current is null || SelectedSpectrum is null ? [] : FrequencyEngine.MatchPeaks(Current, SelectedSpectrum);
    public IReadOnlyList<ComparisonRow> Comparison => Current is null || SelectedScenario is null ? [] : FrequencyEngine.Compare(Current, SelectedScenario);
    public IReadOnlyList<string> QualityNotes => Current is null ? [] : FrequencyEngine.QualityNotes(Current);
    public string StorePath => store.DirectoryPath;

    public WorkspaceViewModel(ProjectStore store, IJSRuntime js)
    {
        this.store = store;
        this.js = js;
        RefreshLibrary();
    }

    public void RefreshLibrary()
    {
        Execute(() =>
        {
            Library = store.List(out var warnings);
            LibraryWarnings = warnings;
        }, "项目库已刷新。" );
    }

    public void NewProject()
    {
        ReplaceGuard(() => SetCurrent(new Project(), null), "新建空白项目会离开当前未保存工作。");
    }

    public void LoadDemo()
    {
        ReplaceGuard(() => SetCurrent(Demonstration.Create(), null), "载入独立模拟示例会离开当前未保存工作。");
    }

    public void DuplicateProject()
    {
        if (Current is null) return;
        var copy = ProjectCodec.Duplicate(Current);
        ReplaceGuard(() => SetCurrent(copy, null), "复制会打开新项目；请先保存原项目中需要保留的修改。");
    }

    public void Open(Guid id)
    {
        ReplaceGuard(() =>
        {
            var read = store.Open(id);
            SetCurrent(read.Project, read.Revision);
            LastPath = Path.Combine(store.DirectoryPath, id.ToString("N") + Software.Extension);
        }, "打开磁盘项目会替换当前未保存工作。");
    }

    public void RestoreBackup(Guid id)
    {
        ReplaceGuard(() => SetCurrent(store.ReadBackupAsCopy(id), null), "读取备份会打开一个副本，替换当前未保存工作。");
    }

    private void ReplaceGuard(Action action, string warning)
    {
        if (Dirty || HasDraftChanges)
        {
            Ask(warning + " 确认继续？", action);
        }
        else
        {
            Execute(action, "工作项目已打开。" );
        }
    }

    private void SetCurrent(Project project, string? readRevision)
    {
        Current = project;
        revision = readRevision;
        savedHash = readRevision is null ? "" : ProjectCodec.Hash(ProjectCodec.Serialize(project));
        LastPath = readRevision is null ? "尚未保存；关闭前请保存或下载项目" : LastPath;
        undo.Clear();
        redo.Clear();
        SelectedObject = "rotor";
        SelectedCandidateId = "electrical";
        SelectedSpectrumId = project.Spectra.FirstOrDefault()?.Id;
        SelectedScenarioId = project.Scenarios.FirstOrDefault()?.Id;
        Preview = null;
        CsvText = "";
        SyncDrafts();
    }

    private void SyncDrafts()
    {
        if (Current is null) return;
        Editor = MotorDraft.From(Current.Motor);
        ProjectName = Current.Name;
        Owner = Current.Owner;
        Purpose = Current.Purpose;
        Tolerance = Labels.Number(Current.Review.MatchToleranceHz);
        PeakCount = Current.Review.MaximumPeaks.ToString();
        ReviewNotes = Current.Review.Notes;
        SpectrumRpm = Labels.Number(Current.Motor.InspectRpm);
    }

    private void Mutate(string action, string detail, Action<Project> change)
    {
        Execute(() =>
        {
            if (Current is null) throw new InvalidDataException("请先新建项目。");
            if (Current.Archived && action != "取消归档") throw new InvalidDataException("项目已归档；请先取消归档，或复制项目。");
            var before = ProjectCodec.Serialize(Current);
            var next = ProjectCodec.Clone(Current);
            change(next);
            next.ModifiedAt = DateTimeOffset.UtcNow;
            next.History.Add(new AuditEvent { Action = action, Detail = detail });
            if (next.History.Count > 2000) next.History.RemoveRange(0, next.History.Count - 2000);
            ProjectValidator.ThrowIfInvalid(next);
            undo.Push(before);
            if (undo.Count > 30)
            {
                var keep = undo.Take(30).Reverse().ToList();
                undo.Clear();
                foreach (var text in keep) undo.Push(text);
            }
            redo.Clear();
            Current = next;
        }, action + "完成；结果已按当前输入更新。" );
    }

    public void ApplyMetadata()
    {
        Mutate("更新项目档案", ProjectName, p =>
        {
            p.Name = ProjectName.Trim();
            p.Owner = Owner.Trim();
            p.Purpose = Purpose.Trim();
        });
    }

    public void ApplyMotor()
    {
        Mutate("应用电机条件", "电机参数或拓扑变更，频率及历史频谱邻近关系重新计算", p => p.Motor = Editor.Build(p.Motor));
    }

    public void ResetDraft()
    {
        SyncDrafts();
        Notify();
    }

    public void SelectObject(string key)
    {
        SelectedObject = key;
        var candidate = Candidates.FirstOrDefault(x => x.ObjectKey == key);
        if (candidate is not null) SelectedCandidateId = candidate.Id;
        Notify();
    }

    public void SelectCandidate(string id)
    {
        var candidate = Candidates.FirstOrDefault(x => x.Id == id);
        if (candidate is null) return;
        SelectedCandidateId = id;
        SelectedObject = candidate.ObjectKey;
        Notify();
    }

    public void SetInspect(string value)
    {
        Mutate("移动观察转速", value + " r/min", p => p.Motor.InspectRpm = MotorDraft.Parse(value, "观察转速"));
        if (Current is not null) Editor.InspectRpm = Labels.Number(Current.Motor.InspectRpm);
    }

    public void ToggleCandidate(string id)
    {
        Mutate("切换候选显示", id, p =>
        {
            if (p.Motor.HiddenCandidates.Contains(id)) p.Motor.HiddenCandidates.Remove(id);
            else p.Motor.HiddenCandidates.Add(id);
        });
    }

    public void AddOrder()
    {
        Mutate("添加自定义候选", OrderName, p => p.CustomOrders.Add(new CustomOrder
        {
            Name = OrderName.Trim(),
            Order = MotorDraft.Parse(OrderValue, "自定义阶次"),
            Basis = OrderBasis.Trim()
        }));
        if (!IsError)
        {
            OrderName = "";
            OrderBasis = "";
        }
    }

    public void RemoveOrder(Guid id)
    {
        Ask("删除该自定义候选？方案快照中的历史副本仍保留。", () => Mutate("删除自定义候选", id.ToString(), p =>
        {
            p.CustomOrders.RemoveAll(x => x.Id == id);
            p.Motor.HiddenCandidates.Remove("custom:" + id);
        }));
    }

    public void AddBand()
    {
        Mutate("添加敏感频段", BandName, p => p.Bands.Add(new SensitiveBand
        {
            Name = BandName.Trim(),
            LowerHz = MotorDraft.Parse(BandLower, "频段下限"),
            UpperHz = MotorDraft.Parse(BandUpper, "频段上限"),
            Basis = BandBasis.Trim(),
            Origin = BandOrigin
        }));
    }

    public void ToggleBand(Guid id)
    {
        Mutate("切换频段", id.ToString(), p =>
        {
            var band = p.Bands.First(x => x.Id == id);
            band.Enabled = !band.Enabled;
        });
    }

    public void RemoveBand(Guid id)
    {
        Ask("删除该敏感频段？", () => Mutate("删除敏感频段", id.ToString(), p => p.Bands.RemoveAll(x => x.Id == id)));
    }

    public async Task ReadCsv(IBrowserFile file)
    {
        await ExecuteAsync(async () =>
        {
            if (!file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 .csv 文件。");
            using var stream = file.OpenReadStream(SpectrumCsv.MaximumBytes);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            CsvText = await reader.ReadToEndAsync();
            CsvFileName = file.Name;
            CsvPickerVersion++;
            if (string.IsNullOrWhiteSpace(SpectrumName)) SpectrumName = Path.GetFileNameWithoutExtension(file.Name);
            Preview = null;
        }, "CSV 已读取。填写元数据后点击预检，确认后导入。" );
    }

    private CsvImportResult ParseCsv()
    {
        return SpectrumCsv.Parse(CsvText, new SpectrumMetadata(SpectrumName.Trim(), MotorDraft.Parse(SpectrumRpm, "频谱转速"), SpectrumUnit, SpectrumReference.Trim(), SpectrumCondition.Trim(), SpectrumOrigin, CsvFileName));
    }

    public void PreviewCsv()
    {
        Execute(() =>
        {
            Preview = ParseCsv();
            if (!Preview.Success) throw new InvalidDataException(string.Join("\n", Preview.Issues.Select(x => x.Path + "：" + x.Message)));
        }, "预检通过；尚未写入项目。请核对频点数和元数据，再确认导入。" );
    }

    public void CommitCsv()
    {
        Mutate("导入频谱", CsvFileName + "；所有点内嵌保存", p =>
        {
            var parsed = ParseCsv();
            if (!parsed.Success) throw new InvalidDataException(string.Join("\n", parsed.Issues.Select(x => x.Path + "：" + x.Message)));
            p.Spectra.Add(parsed.Spectrum!);
            SelectedSpectrumId = parsed.Spectrum!.Id;
        });
        if (!IsError) Preview = null;
    }

    public void SelectSpectrum(Guid id)
    {
        SelectedSpectrumId = id;
        Notify();
    }

    public void ToggleSpectrum(Guid id)
    {
        Mutate("切换频谱叠加", id.ToString(), p =>
        {
            var spectrum = p.Spectra.First(x => x.Id == id);
            spectrum.Visible = !spectrum.Visible;
        });
    }

    public void RemoveSpectrum(Guid id)
    {
        Ask("删除该频谱及其内嵌频点？", () =>
        {
            Mutate("删除频谱", id.ToString(), p => p.Spectra.RemoveAll(x => x.Id == id));
            if (SelectedSpectrumId == id) SelectedSpectrumId = Current?.Spectra.FirstOrDefault()?.Id;
        });
    }

    public void CaptureScenario()
    {
        Mutate("记录方案快照", ScenarioName, p =>
        {
            var scenario = new Scenario
            {
                Name = ScenarioName.Trim(),
                Motor = ProjectCodec.Clone(p.Motor),
                CustomOrders = ProjectCodec.Clone(p.CustomOrders),
                Demonstration = p.Demonstration
            };
            p.Scenarios.Add(scenario);
            SelectedScenarioId = scenario.Id;
        });
    }

    public void SelectScenario(Guid id)
    {
        SelectedScenarioId = id;
        Notify();
    }

    public void RemoveScenario(Guid id)
    {
        Ask("删除该方案快照？", () =>
        {
            Mutate("删除方案快照", id.ToString(), p => p.Scenarios.RemoveAll(x => x.Id == id));
            if (SelectedScenarioId == id) SelectedScenarioId = Current?.Scenarios.FirstOrDefault()?.Id;
        });
    }

    public void ApplyReview()
    {
        Mutate("更新复核口径", "重新筛选谱峰邻近候选，不改变原始频点", p =>
        {
            p.Review.MatchToleranceHz = MotorDraft.Parse(Tolerance, "绝对频差容差");
            p.Review.MaximumPeaks = MotorDraft.ParseInt(PeakCount, "谱峰数量");
            p.Review.Notes = ReviewNotes;
        });
    }

    public void ToggleArchive()
    {
        if (Current is null) return;
        var archived = Current.Archived;
        Ask(archived ? "取消项目归档并继续编辑？" : "归档后将暂停内容编辑，仍可保存、下载和复制。确认归档？", () => Mutate(archived ? "取消归档" : "归档项目", "用户确认", p => p.Archived = !p.Archived));
    }

    public void Undo()
    {
        Execute(() =>
        {
            if (Current is null || undo.Count == 0) return;
            redo.Push(ProjectCodec.Serialize(Current));
            var restored = ProjectCodec.Parse(undo.Pop());
            restored.History = ProjectCodec.Clone(Current.History);
            restored.History.Add(new AuditEvent { Action = "撤销", Detail = "恢复上一个输入状态；审计记录保留" });
            if (restored.History.Count > 2000) restored.History.RemoveRange(0, restored.History.Count - 2000);
            restored.ModifiedAt = DateTimeOffset.UtcNow;
            Current = restored;
            SyncDrafts();
        }, "已撤销；请检查后保存。" );
    }

    public void Redo()
    {
        Execute(() =>
        {
            if (Current is null || redo.Count == 0) return;
            undo.Push(ProjectCodec.Serialize(Current));
            var restored = ProjectCodec.Parse(redo.Pop());
            restored.History = ProjectCodec.Clone(Current.History);
            restored.History.Add(new AuditEvent { Action = "重做", Detail = "恢复下一输入状态；审计记录保留" });
            if (restored.History.Count > 2000) restored.History.RemoveRange(0, restored.History.Count - 2000);
            restored.ModifiedAt = DateTimeOffset.UtcNow;
            Current = restored;
            SyncDrafts();
        }, "已重做；请检查后保存。" );
    }

    public void Save()
    {
        Execute(() =>
        {
            if (Current is null) throw new InvalidDataException("请先新建项目。");
            var written = store.Save(Current, revision);
            revision = written.Revision;
            savedHash = ProjectCodec.Hash(ProjectCodec.Serialize(Current));
            LastPath = written.Path;
            Library = store.List(out var warnings);
            LibraryWarnings = warnings;
        }, "已保存到本机项目库；再次保存会保留上一版 .bak。未应用的输入草稿不包含在保存中。" );
    }

    public async Task ImportProject(IBrowserFile file)
    {
        await ExecuteAsync(async () =>
        {
            if (!file.Name.EndsWith(Software.Extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 .motorproj 项目文件。");
            using var stream = file.OpenReadStream(ProjectCodec.MaximumFileBytes);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var text = await reader.ReadToEndAsync();
            _ = ProjectCodec.Parse(text);
            pendingProjectText = text;
            ReplaceGuard(() =>
            {
                SetCurrent(ProjectCodec.Parse(pendingProjectText!), null);
                pendingProjectText = null;
            }, "导入将替换当前工作。相同标识的磁盘文件不会被直接覆盖。");
        }, "项目已校验；若出现确认框，请决定是否离开当前工作。" );
    }

    public async Task Export(string kind)
    {
        await ExecuteAsync(async () =>
        {
            if (kind == "template")
            {
                await Download("频谱导入模板.csv", "text/csv;charset=utf-8", SpectrumCsv.Template(), true);
                return;
            }
            if (Current is null) throw new InvalidDataException("请先打开项目。");
            var name = ExportService.SafeName(Current.Name);
            switch (kind)
            {
                case "review":
                    await Download(name + "-输入核对.json", "application/json", ProjectReview.Json(Current));
                    break;
                case "project":
                    await Download(name + Software.Extension, "application/json", ProjectCodec.Serialize(Current));
                    break;
                case "frequency":
                    await Download(name + "-频率扫描.csv", "text/csv;charset=utf-8", ExportService.FrequencyCsv(Current), true);
                    break;
                case "crossing":
                    await Download(name + "-频段交叠.csv", "text/csv;charset=utf-8", ExportService.CrossingCsv(Current), true);
                    break;
                case "evidence":
                    await Download(name + "-谱峰核对.csv", "text/csv;charset=utf-8", ExportService.EvidenceCsv(Current), true);
                    break;
                case "comparison":
                    if (SelectedScenario is null) throw new InvalidDataException("请先选中方案快照。");
                    await Download(name + "-方案比较.csv", "text/csv;charset=utf-8", ExportService.ComparisonCsv(Current, SelectedScenario), true);
                    break;
                case "svg":
                    await Download(name + "-频率图.svg", "image/svg+xml;charset=utf-8", new ChartGeometry(Current.Motor).Svg(Current));
                    break;
                case "html":
                    await Download(name + "-复核报告.html", "text/html;charset=utf-8", ExportService.Html(Current));
                    break;
                case "raw":
                    if (SelectedSpectrum is null) throw new InvalidDataException("请先选中频谱。");
                    await Download(ExportService.SafeName(SelectedSpectrum.Name) + "-原始频点.csv", "text/csv;charset=utf-8", SpectrumCsv.Export(SelectedSpectrum), true);
                    break;
                default:
                    throw new InvalidDataException("不支持的导出类型。");
            }
        }, "已发起浏览器下载；目标为浏览器下载目录，请在下载列表核实文件。导出使用已应用输入。" );
    }

    private async Task Download(string name, string mime, string text, bool bom = false)
    {
        await js.InvokeVoidAsync("motorFiles.download", name, mime, (bom ? "\uFEFF" : "") + text);
    }

    private void Ask(string text, Action action)
    {
        Confirmation = text;
        pending = action;
        Notify();
    }

    public void Confirm()
    {
        var action = pending;
        pending = null;
        Confirmation = "";
        if (action is not null)
        {
            Execute(action, null);
        }
    }

    public void Cancel()
    {
        pending = null;
        pendingProjectText = null;
        Confirmation = "";
        Notify();
    }

    private void Execute(Action action, string? success)
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
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Message = ex.Message;
            IsError = true;
        }
        Notify();
    }

    private async Task ExecuteAsync(Func<Task> action, string success)
    {
        try
        {
            await action();
            Message = success;
            IsError = false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JSException)
        {
            Message = ex.Message;
            IsError = true;
        }
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
