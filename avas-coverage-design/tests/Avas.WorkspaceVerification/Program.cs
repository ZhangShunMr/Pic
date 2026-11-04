using System.Text;
using Avas.Core.Analysis;
using Avas.Core.Domain;
using Avas.Core.IO;
using Avas.Workspace;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/workspace-verification.json");
var root = Path.Combine(Path.GetTempPath(), "avas-workspace-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = new List<object>();
var failures = 0;
void Assert(bool condition, string message = "断言失败")
{
    if (!condition) throw new Exception(message);
}
async Task Check(string name, Func<Task> action)
{
    try { await action(); checks.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; checks.Add(new { name, status = "failed", error = ex.Message }); Console.WriteLine("FAIL " + name + " " + ex.Message); }
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
(FakeFiles Files, WorkspaceViewModel Vm) Create()
{
    var files = new FakeFiles(Path.Combine(root, Guid.NewGuid().ToString("N")));
    var vm = new WorkspaceViewModel(new ProjectStore(files.Root), files);
    return (files, vm);
}
void Good(WorkspaceViewModel vm) => Assert(vm.Errors.Count == 0, string.Join("\n", vm.Errors));
try
{
    await Check("普通启动空白且可直接关闭", () => Sync(() =>
    {
        var (_, vm) = Create();
        Assert(vm.Project.Speakers.Count == 0 && vm.Project.Listeners.Count == 0 && !vm.Dirty && vm.CanClose());
    }));
    await Check("空源计算拒绝而不制造结果", async () =>
    {
        var (_, vm) = Create(); await vm.CalculateAsync(); Assert(vm.Errors.Count > 0 && vm.Result is null && !vm.Busy);
    });
    await Check("完整空白到解析案例保存重开导出", async () =>
    {
        var (files, vm) = Create();
        vm.Draft.Name = "解析验证"; vm.ApplyDraft(); Good(vm);
        vm.AddSpeaker(); Good(vm);
        vm.SpeakerDraft!.X = 0; vm.SpeakerDraft.Y = 0; vm.SpeakerDraft.Height = 1.5;
        vm.SpeakerDraft.Exponent = 0; vm.SpeakerDraft.Source = "人工解析核对：1m各带60dB";
        vm.ApplyDraft(); Good(vm);
        vm.Draft.Settings.SelectedHz = 1000; vm.Draft.Settings.BackgroundSource = "人工解析背景，各带35dB";
        vm.ApplyDraft(); Good(vm);
        vm.AddListener(); vm.ListenerDraft!.Name = "2m核对点"; vm.ListenerDraft.X = 2; vm.ListenerDraft.Y = 0;
        vm.ApplyDraft(); Good(vm); await vm.CalculateAsync(); Good(vm);
        Assert(Math.Abs(vm.CurrentPoint!.Signal - 53.9794000867) < 1e-8);
        vm.Save(); Good(vm); Assert(!vm.Dirty && vm.Library.Count == 1);
        vm.Export("project"); Good(vm);
        var external = files.Written.Last().Value;
        var (_, moved) = Create(); moved.ImportProject(external); Good(moved);
        await moved.CalculateAsync(); Good(moved);
        Assert(Math.Abs(moved.CurrentPoint!.Signal - vm.CurrentPoint.Signal) < 1e-10);
        vm.New(false); vm.Open(vm.Library.Single().Id is { } id ? Guid.Parse(id) : Guid.Empty, false); Good(vm);
        Assert(vm.Project.Name == "解析验证" && !vm.Dirty);
        await vm.CalculateAsync(); Good(vm);
        foreach (var kind in new[] { "report", "grid", "points", "svg", "json" }) { vm.Export(kind); Good(vm); }
        Assert(files.Written.Count == 6);
        Assert(files.Written.Keys.Any(x => x.EndsWith(".svg")) && Encoding.UTF8.GetString(files.Written.Single(x => x.Key.EndsWith(".html")).Value).Contains("人工解析"));
    });
    await Check("无效应用不损坏项目且草稿保留", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.AddSpeaker(); var before = ProjectCodec.Json(vm.Project);
        vm.SpeakerDraft!.ReferenceDistance = 0; vm.ApplyDraft();
        Assert(vm.Errors.Count > 0 && vm.HasDraft && ProjectCodec.Json(vm.Project) == before);
        vm.Save(); Assert(vm.Errors.Count > 0 && vm.Library.Count == 0);
        vm.DiscardDraft(); Good(vm); Assert(!vm.HasDraft && vm.SpeakerDraft.ReferenceDistance == 1);
    }));
    await Check("绑定格式错误阻止旧数值被计算保存", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.BindingErrorCount = 1; vm.AddSpeaker(); Assert(vm.Errors.Count > 0 && vm.Project.Speakers.Count == 0);
        vm.ApplyDraft(); Assert(vm.Errors.Count > 0); vm.DiscardDraft(); Assert(vm.BindingErrorCount == 0);
    }));
    await Check("有草稿时导航与切换对象拒绝", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.AddSpeaker(); vm.AddSpeaker(); vm.Draft.Name = "草稿";
        vm.SelectedSection = "扬声器"; Assert(vm.Tab == "项目" && vm.Errors.Count > 0);
        var id = vm.SelectedSpeakerId; vm.SelectedSpeaker = vm.Draft.Speakers.First(x => x.Id != id);
        Assert(vm.SelectedSpeakerId == id && vm.Errors.Count > 0);
    }));
    await Check("替换确认取消保全草稿", () => Sync(() =>
    {
        var (files, vm) = Create(); vm.Draft.Name = "不能丢失"; files.Accept = false;
        vm.Commands["Demo"].Execute(null); Assert(vm.Draft.Name == "不能丢失" && vm.Project.Speakers.Count == 0);
        Assert(!vm.CanClose());
    }));
    await Check("模拟入口、归档、撤销和重做", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.New(true); Assert(vm.Project.Speakers.All(x => x.Origin == Origin.Simulation));
        var count = vm.Project.Speakers.Count; vm.AddSpeaker(); vm.Undo(); Assert(vm.Project.Speakers.Count == count);
        vm.Redo(); Assert(vm.Project.Speakers.Count == count + 1);
        vm.ToggleArchive(); vm.AddListener(); Assert(vm.Errors.Count > 0); vm.ToggleArchive(); vm.AddListener(); Good(vm);
    }));
    await Check("修改后隐藏过期结果并阻止导出", async () =>
    {
        var (_, vm) = Create(); vm.New(true); await vm.CalculateAsync(); Assert(vm.CurrentResult is not null);
        vm.SpeakerDraft!.Gain += 1; Assert(vm.CurrentResult is null); vm.Export("grid"); Assert(vm.Errors.Count > 0);
        vm.ApplyDraft(); Assert(vm.Stale); vm.Export("report"); Assert(vm.Errors.Count > 0);
    });
    await Check("快照比较差值及不一致条件阻断", async () =>
    {
        var (_, vm) = Create(); vm.New(true); await vm.CalculateAsync(); vm.SnapshotName = "基线"; vm.CaptureSnapshot(); Good(vm);
        vm.SpeakerDraft!.Gain = 3; vm.ApplyDraft(); await vm.CalculateAsync(); vm.SnapshotName = "目标"; vm.CaptureSnapshot(); vm.Compare(); Good(vm);
        Assert(vm.Comparison is { Comparable: true });
        vm.Draft.Settings.Step = 1; vm.ApplyDraft(); await vm.CalculateAsync(); vm.SnapshotName = "不同网格"; vm.CaptureSnapshot(); vm.Compare();
        Assert(vm.Comparison is { Comparable: false });
    });
    await Check("扫描应用只在有效结果时可用", async () =>
    {
        var (_, vm) = Create(); vm.New(true);
        vm.ScanRequest = new ScanRequest { SpeakerId = vm.SelectedSpeakerId!.Value, Parameter = ScanParameter.Gain, Start = 0, End = 2, Step = 1 };
        var old = vm.Project.Speakers.First().Gain; await vm.RunScanAsync(); Good(vm);
        Assert(vm.Scan!.Rows.Count == 3 && vm.Project.Speakers.First().Gain == old);
        vm.ApplyScan(2); Good(vm); Assert(vm.Project.Speakers.First().Gain == 2 && vm.Scan is null);
    });
    await Check("观察点与频谱CSV预检原子应用", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.AddSpeaker();
        vm.CsvText = SpectrumCsv.Template(); vm.PreviewCsv(); vm.CsvText += "\n125,1,1"; vm.ApplyCsv(); Assert(vm.Errors.Count > 0);
        vm.CsvText = SpectrumCsv.Template(); vm.PreviewCsv(); vm.ApplyCsv(); Good(vm);
        vm.ListenerText = ListenerCsv.Template(); vm.PreviewListeners(); vm.ApplyListeners(); Good(vm); Assert(vm.Project.Listeners.Count == 2);
    }));
    await Check("新增加声源和环点上限保全已有数据", () => Sync(() =>
    {
        var (_, vm) = Create(); for (var i = 0; i < 8; i++) vm.AddSpeaker(); vm.AddSpeaker(); Assert(vm.Project.Speakers.Count == 8 && vm.Errors.Count > 0);
        vm.AddRing(5, 36); vm.AddRing(5, 36); vm.AddRing(5, 36); Assert(vm.Project.Listeners.Count == 72 && vm.Errors.Count > 0);
    }));
    await Check("保存备份重开、磁盘冲突与复制解除冲突", () => Sync(() =>
    {
        var (files, vm) = Create(); vm.Save(); Good(vm); vm.Draft.Note = "第二版"; vm.ApplyDraft(); vm.Save(); Good(vm);
        Assert(vm.Library.Single().HasBackup); var id = vm.Project.Id;
        var path = Directory.GetFiles(files.Root, "*.avasproj").Single(); var altered = ProjectCodec.Decode(File.ReadAllBytes(path)); altered.Note = "外部修改"; File.WriteAllBytes(path, ProjectCodec.Encode(altered));
        vm.Save(); Assert(vm.Errors.Count > 0); vm.CopyProject(); vm.Save(); Good(vm); Assert(vm.Project.Id != id && vm.Library.Count == 2);
        vm.Open(id, true); Good(vm); Assert(vm.Project.Note == "" && vm.Dirty);
    }));
    await Check("文件取消反馈不声称保存成功", () => Sync(() =>
    {
        var (files, vm) = Create(); files.CancelWrite = true; vm.Export("project"); Assert(vm.Status.Contains("取消") && vm.Errors.Count == 0 && files.Written.Count == 0);
    }));
    await Check("试听配置变化立即使旧试听无效", async () =>
    {
        var (_, vm) = Create(); vm.New(true); await vm.CalculateAsync(); vm.AuditionSeconds = 1; await vm.RenderAuditionAsync(); Good(vm); Assert(vm.Audition is not null);
        vm.PlaybackReference += 3; Assert(vm.Audition is null && vm.AudioUrl is null); vm.Commands["Play"].Execute(null); Assert(vm.Errors.Count > 0);
    });
    await Check("并行命令忙状态被禁用并恢复", async () =>
    {
        var (_, vm) = Create(); vm.New(true); var task = vm.CalculateAsync();
        Assert(!vm.Commands["New"].CanExecute(null)); await task; Assert(vm.Commands["New"].CanExecute(null) && !vm.Busy); Good(vm);
    });
    await Check("损坏文件导入保全现有工程", () => Sync(() =>
    {
        var (_, vm) = Create(); vm.New(true); var id = vm.Project.Id; vm.ImportProject(Encoding.UTF8.GetBytes("{bad}")); Assert(vm.Project.Id == id && vm.Errors.Count > 0);
    }));
}
finally
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, ProjectCodec.Json(new { date = DateTimeOffset.UtcNow, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, total = checks.Count, failed = failures, checks }));
    Directory.Delete(root, true);
}
Console.WriteLine($"{checks.Count - failures}/{checks.Count} passed");
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class FakeFiles(string root) : IDesktopServices
{
    public string Root { get; } = root;
    public string LastReadName => "test.wav";
    public bool Accept { get; set; } = true;
    public bool CancelWrite { get; set; }
    public Dictionary<string, byte[]> Written { get; } = [];
    public bool Confirm(string title, string message) => Accept;
    public byte[]? ReadFile(string kind) => null;
    public string WriteExport(byte[] bytes, string suggestedName)
    {
        if (CancelWrite) throw new OperationCanceledException();
        Written[suggestedName] = bytes;
        return Path.Combine(Root, suggestedName);
    }
    public string WriteTemporaryAudio(byte[] bytes) => "temporary-test.wav";
    public void PlayAudio(string path) { }
    public void StopAudio() { }
}
