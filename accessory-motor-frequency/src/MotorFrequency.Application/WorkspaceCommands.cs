using System.Text;
using System.Text.Json;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Application;

public sealed partial class WorkspaceViewModel
{
    public RelayCommand NewCommand { get; private set; } = null!;
    public RelayCommand DemoCommand { get; private set; } = null!;
    public RelayCommand OpenCommand { get; private set; } = null!;
    public RelayCommand SaveCommand { get; private set; } = null!;
    public RelayCommand SaveAsCommand { get; private set; } = null!;
    public RelayCommand DuplicateCommand { get; private set; } = null!;
    public RelayCommand ApplyCommand { get; private set; } = null!;
    public RelayCommand ResetCommand { get; private set; } = null!;
    public RelayCommand UndoCommand { get; private set; } = null!;
    public RelayCommand RedoCommand { get; private set; } = null!;
    public RelayCommand ArchiveCommand { get; private set; } = null!;
    public RelayCommand AddOrderCommand { get; private set; } = null!;
    public RelayCommand RemoveOrderCommand { get; private set; } = null!;
    public RelayCommand ToggleCandidateCommand { get; private set; } = null!;
    public RelayCommand AddBandCommand { get; private set; } = null!;
    public RelayCommand RemoveBandCommand { get; private set; } = null!;
    public RelayCommand ToggleBandCommand { get; private set; } = null!;
    public RelayCommand InspectCrossingCommand { get; private set; } = null!;
    public RelayCommand InspectRpmCommand { get; private set; } = null!;
    public RelayCommand ReadCsvCommand { get; private set; } = null!;
    public RelayCommand PreviewCsvCommand { get; private set; } = null!;
    public RelayCommand ImportCsvCommand { get; private set; } = null!;
    public RelayCommand RemoveSpectrumCommand { get; private set; } = null!;
    public RelayCommand ToggleSpectrumCommand { get; private set; } = null!;
    public RelayCommand CaptureCommand { get; private set; } = null!;
    public RelayCommand RemoveScenarioCommand { get; private set; } = null!;
    public RelayCommand SweepCommand { get; private set; } = null!;
    public RelayCommand ExportCommand { get; private set; } = null!;
    public RelayCommand TemplateCommand { get; private set; } = null!;
    public RelayCommand RevealCommand { get; private set; } = null!;

    private RelayCommand Command(Action action, Func<bool>? enabled = null)
    {
        var command = new RelayCommand(() => Run(action), enabled);
        commands.Add(command);
        return command;
    }

    private void InitializeCommands()
    {
        NewCommand = Command(() => Replace(() => new Project(), "新建空白项目"));
        DemoCommand = Command(() => Replace(Demonstration.Create, "载入独立模拟示例"));
        OpenCommand = Command(OpenFile);
        SaveCommand = Command(() => Save(false), () => HasProject);
        SaveAsCommand = Command(() => Save(true), () => HasProject);
        DuplicateCommand = Command(Duplicate, () => HasProject);
        ApplyCommand = Command(ApplyAll, () => Editable);
        ResetCommand = Command(() =>
        {
            if (HasDraftChanges && !interaction.Confirm("放弃所有尚未应用的输入和新增表单？已应用的项目结果不变。"))
            {
                return;
            }
            SynchronizeDrafts(true);
            preview = null;
            Message = "草稿已重置为已应用输入。";
        }, () => HasProject);
        UndoCommand = Command(() => Restore(undo, redo, "撤销"), () => undo.Count > 0 && Editable);
        RedoCommand = Command(() => Restore(redo, undo, "重做"), () => redo.Count > 0 && Editable);
        ArchiveCommand = Command(ToggleArchive, () => HasProject);
        AddOrderCommand = Command(AddOrder, () => Editable);
        RemoveOrderCommand = Command(RemoveOrder, () => Editable);
        ToggleCandidateCommand = Command(ToggleCandidate, () => Editable);
        AddBandCommand = Command(AddBand, () => Editable);
        RemoveBandCommand = Command(RemoveBand, () => Editable);
        ToggleBandCommand = Command(ToggleBand, () => Editable);
        InspectCrossingCommand = Command(InspectCrossing, () => Editable);
        InspectRpmCommand = new RelayCommand(value => Run(() => SetInspect(Convert.ToDouble(value,
            System.Globalization.CultureInfo.InvariantCulture))), _ => Editable);
        commands.Add(InspectRpmCommand);
        ReadCsvCommand = Command(ReadCsv, () => Editable);
        PreviewCsvCommand = Command(PreviewCsv, () => Editable);
        ImportCsvCommand = Command(CommitCsv, () => Editable);
        RemoveSpectrumCommand = Command(RemoveSpectrum, () => Editable);
        ToggleSpectrumCommand = Command(ToggleSpectrum, () => Editable);
        CaptureCommand = Command(Capture, () => Editable);
        RemoveScenarioCommand = Command(RemoveScenario, () => Editable);
        SweepCommand = Command(CalculateSweep, () => HasProject);
        ExportCommand = Command(Export, () => HasProject);
        TemplateCommand = Command(() => WriteExport("频谱导入模板", ".csv", "CSV模板", SpectrumCsv.Template()));
        RevealCommand = Command(() => interaction.RevealFile(LastOutput), () => File.Exists(LastOutput));
    }

    public bool TryClose()
    {
        var leave = false;
        Run(() => leave = CanLeave("关闭软件"));
        return leave;
    }

    private bool CanLeave(string reason)
    {
        if (!Dirty && !HasDraftChanges)
        {
            return true;
        }
        var choice = interaction.ConfirmLeave(reason + "将离开当前项目。选择保存时会先校验并应用档案、电机和复核草稿；新增候选、频带、CSV与快照表单请先完成添加或重置。");
        if (choice == LeaveChoice.Cancel)
        {
            return false;
        }
        if (choice == LeaveChoice.Discard)
        {
            return true;
        }
        return Save(false);
    }

    private void Replace(Func<Project> build, string reason)
    {
        if (!CanLeave(reason))
        {
            return;
        }
        var project = build();
        ProjectValidator.ThrowIfInvalid(project);
        file.Detach();
        SetCurrent(project);
        Section = "overview";
        Message = project.Demonstration ? "已载入独立模拟示例，不能证明实车效果。" : "已建立空白项目，不含示例频谱或频带。";
        IsError = false;
    }

    private void SetCurrent(Project project)
    {
        ProjectValidator.ThrowIfInvalid(project);
        current = project;
        undo.Clear();
        redo.Clear();
        preview = null;
        previewHash = "";
        sweepContext = null;
        sweepRequest = null;
        sweepRows = [];
        reverseRows = [];
        selectedCandidate = FrequencyEngine.Candidates(project.Motor, project.CustomOrders).FirstOrDefault();
        selectedOrder = project.CustomOrders.FirstOrDefault();
        selectedBand = project.Bands.FirstOrDefault();
        selectedSpectrum = project.Spectra.FirstOrDefault();
        selectedScenario = project.Scenarios.FirstOrDefault();
        selectedCrossing = null;
        SynchronizeDrafts(true);
    }

    private void OpenFile()
    {
        var path = interaction.OpenProjectFile();
        if (path is null || !CanLeave("打开项目"))
        {
            return;
        }
        if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
        {
            var backup = new DocumentFile().Open(path);
            var copy = ProjectCodec.Duplicate(backup);
            copy.Name = Truncate(copy.Name, 90) + "（备份恢复）";
            file.Detach();
            SetCurrent(copy);
            Message = "备份已作为新副本打开；请选择新路径保存。";
        }
        else
        {
            var project = file.Open(path);
            SetCurrent(project);
            Message = "项目已从本机文件打开，原始频谱由项目内嵌恢复。";
        }
        IsError = false;
    }

    private bool Save(bool saveAs)
    {
        if (current is null)
        {
            return false;
        }
        if (HasEntryDraftChanges)
        {
            throw new InvalidDataException("仍有新增表单未完成。请完成候选/频带添加、频谱导入、快照记录，或使用“重置草稿”放弃后再保存。");
        }
        if (HasAppliedDraftChanges)
        {
            if (!Editable)
            {
                throw new InvalidDataException("项目已归档，请重置草稿或取消归档后保存。");
            }
            ApplyAll();
        }
        var path = !saveAs ? file.PathName : null;
        path ??= interaction.SaveFile(ExportService.SafeName(current.Name), Software.Extension, "电机频率项目");
        if (path is null)
        {
            return false;
        }
        var same = file.PathName is not null && string.Equals(Path.GetFullPath(path), file.PathName,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var overwrite = same;
        if (File.Exists(path) && !same)
        {
            overwrite = interaction.Confirm("目标已存在，确认覆盖并保留旧文件为 .bak？\n" + path);
            if (!overwrite)
            {
                return false;
            }
        }
        var output = file.Save(current, path, overwrite);
        dirtyReference = null;
        LastOutput = output;
        Message = "已保存并写入磁盘：" + output;
        IsError = false;
        return true;
    }

    private void Duplicate()
    {
        if (current is null)
        {
            return;
        }
        if (!CanLeave("复制当前已应用项目"))
        {
            return;
        }
        var copy = ProjectCodec.Duplicate(current);
        copy.Name = Truncate(copy.Name, 100);
        file.Detach();
        SetCurrent(copy);
        Message = "已建立新标识副本，请另行保存。";
    }

    private void Mutate(string action, string detail, Action<Project> change, bool allowArchived = false)
    {
        if (current is null || (current.Archived && !allowArchived))
        {
            throw new InvalidDataException("请先建立项目；已归档项目需先取消归档。");
        }
        var before = ProjectCodec.Serialize(current);
        var next = ProjectCodec.Clone(current);
        change(next);
        next.ModifiedAt = DateTimeOffset.UtcNow;
        next.SoftwareVersion = Software.Version;
        AppendHistory(next, action, detail);
        ProjectValidator.ThrowIfInvalid(next);
        undo.Push(before);
        TrimStack(undo);
        redo.Clear();
        current = next;
        selectedCandidate = Candidates.FirstOrDefault(x => x.Id == selectedCandidate?.Id) ?? Candidates.FirstOrDefault();
        selectedOrder = current.CustomOrders.FirstOrDefault(x => x.Id == selectedOrder?.Id);
        selectedBand = current.Bands.FirstOrDefault(x => x.Id == selectedBand?.Id);
        selectedSpectrum = current.Spectra.FirstOrDefault(x => x.Id == selectedSpectrum?.Id);
        selectedScenario = current.Scenarios.FirstOrDefault(x => x.Id == selectedScenario?.Id);
        selectedCrossing = Crossings.FirstOrDefault(x => x.CandidateId == selectedCrossing?.CandidateId && x.BandId == selectedCrossing?.BandId);
        Message = action + "完成；结果按已应用输入更新。";
        IsError = false;
    }

    private void ApplyAll()
    {
        if (current is null)
        {
            return;
        }
        var motor = Motor.Build(current.Motor);
        var tolerance = MotorDraft.Number(Review.Tolerance, "绝对容差 Hz");
        var peaks = MotorDraft.Integer(Review.PeakCount, "谱峰数量");
        Mutate("应用项目条件", "档案、电机和复核口径；按当前条件重新计算，不改写原始频点", p =>
        {
            p.Name = Metadata.Name.Trim();
            p.Owner = Metadata.Owner.Trim();
            p.Purpose = Metadata.Purpose.Trim();
            p.Motor = motor;
            p.Review.MatchToleranceHz = tolerance;
            p.Review.MaximumPeaks = peaks;
            p.Review.Notes = Review.Notes;
        });
        SynchronizeDrafts(false);
    }

    private void ToggleArchive()
    {
        if (current is null)
        {
            return;
        }
        if (HasDraftChanges)
        {
            throw new InvalidDataException("归档变更前请先应用或重置草稿，完成新增表单。");
        }
        if (!interaction.Confirm(current.Archived ? "取消归档，恢复编辑？" : "归档后暂停内容编辑，仍可保存、导出与复制。确认归档？"))
        {
            return;
        }
        Mutate(current.Archived ? "取消归档" : "归档项目", "用户确认", p => p.Archived = !p.Archived, true);
    }

    private void Restore(Stack<string> source, Stack<string> target, string action)
    {
        if (current is null || source.Count == 0)
        {
            return;
        }
        if (HasDraftChanges && !interaction.Confirm("撤销/重做将放弃所有尚未应用输入和新增表单，继续？"))
        {
            return;
        }
        var restored = ProjectCodec.Parse(source.Peek());
        restored.History = ProjectCodec.Clone(current.History);
        restored.ModifiedAt = DateTimeOffset.UtcNow;
        AppendHistory(restored, action, "输入状态恢复；已有操作记录保留");
        ProjectValidator.ThrowIfInvalid(restored);
        source.Pop();
        target.Push(ProjectCodec.Serialize(current));
        TrimStack(target);
        current = restored;
        selectedSpectrum = current.Spectra.FirstOrDefault(x => x.Id == selectedSpectrum?.Id) ?? current.Spectra.FirstOrDefault();
        selectedScenario = current.Scenarios.FirstOrDefault(x => x.Id == selectedScenario?.Id) ?? current.Scenarios.FirstOrDefault();
        selectedBand = null;
        selectedOrder = null;
        selectedCandidate = Candidates.FirstOrDefault();
        preview = null;
        SynchronizeDrafts(true);
        Message = "已" + action + "，请复核并保存。";
    }

    private static void AppendHistory(Project project, string action, string detail)
    {
        project.History.Add(new AuditEvent { Action = action, Detail = detail });
        if (project.History.Count > 2000)
        {
            project.History.RemoveRange(0, project.History.Count - 2000);
        }
    }

    private static void TrimStack(Stack<string> stack)
    {
        if (stack.Count <= 30)
        {
            return;
        }
        var keep = stack.Take(30).Reverse().ToArray();
        stack.Clear();
        foreach (var item in keep)
        {
            stack.Push(item);
        }
    }

    private static string Truncate(string text, int length)
    {
        return text[..Math.Min(text.Length, length)];
    }
}
