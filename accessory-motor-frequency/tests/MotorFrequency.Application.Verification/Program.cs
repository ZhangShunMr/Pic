using System.Text;
using System.Text.Json;
using MotorFrequency.Application;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

var results = new List<object>();
var failed = 0;
var root = Path.Combine(Path.GetTempPath(), "motor-native-verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Assert(bool condition, string reason = "断言不符")
{
    if (!condition) throw new Exception(reason);
}
void Near(double actual, double expected) => Assert(Math.Abs(actual - expected) < 1e-8, $"{actual} != {expected}");
void Check(string name, Action action)
{
    try
    {
        action();
        results.Add(new { name, passed = true });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failed++;
        results.Add(new { name, passed = false, reason = error.Message });
        Console.WriteLine("FAIL " + name + " " + error.Message);
    }
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or DecoderFallbackException) { return; }
    throw new Exception("应拒绝而未拒绝");
}
WorkspaceViewModel New(FakeInteraction ui)
{
    var vm = new WorkspaceViewModel(ui);
    vm.NewCommand.Execute(null);
    return vm;
}
try
{
    Check("启动无项目且保存禁用", () =>
    {
        var vm = new WorkspaceViewModel(new FakeInteraction());
        Assert(vm.Current is null && !vm.SaveCommand.CanExecute(null));
    });
    Check("新建不夹带示例频谱频带快照", () =>
    {
        var vm = New(new());
        Assert(vm.Current is { Demonstration: false } && vm.Current.Spectra.Count == 0
            && vm.Current.Bands.Count == 0 && vm.Current.Scenarios.Count == 0);
    });
    Check("原生业务完整闭环保存重开及报告导出", () =>
    {
        var ui = new FakeInteraction { SavePath = Path.Combine(root, "closed-loop.motorproj") };
        var vm = New(ui);
        vm.Metadata.Name = "1800转频率复核";
        vm.Metadata.Purpose = "独立手算候选与频段交叠";
        vm.Motor.IncludeCogging = true;
        vm.Motor.Symmetric = true;
        vm.Motor.Assumptions = "测试理想对称12槽4极；非实测";
        vm.ApplyCommand.Execute(null);
        Assert(!vm.IsError && !vm.HasAppliedDraftChanges);
        Near(vm.FrequencyRows.Single(x => x.CandidateId == "electrical").FrequencyHz, 60);
        vm.Band.Name = "关注频带";
        vm.Band.Basis = "人工测试频带";
        vm.AddBandCommand.Execute(null);
        var crossing = vm.Crossings.Single(x => x.CandidateId == "electrical");
        Near(crossing.LowerRpm, 1650);
        Near(crossing.UpperRpm, 1950);
        vm.Import.Name = "模拟三点峰";
        vm.Import.Reference = "模拟dB刻度，非校准";
        vm.Import.Condition = "离线公式测试";
        vm.Import.Origin = DataOrigin.Simulated;
        vm.Import.Csv = "frequency_hz,amplitude\n59,1\n60,10\n61,1\n";
        vm.PreviewCsvCommand.Execute(null);
        Assert(vm.Preview?.Success == true);
        vm.ImportCsvCommand.Execute(null);
        Assert(vm.Current!.Spectra.Count == 1 && vm.PeakMatches.Any(x => x.CandidateName == "电气基频"));
        vm.ScenarioName = "两极对基准";
        vm.CaptureCommand.Execute(null);
        vm.Motor.PolePairs = "3";
        vm.ApplyCommand.Execute(null);
        Near(vm.Comparison.Single(x => x.Name == "电气基频").DifferenceHz!.Value, 30);
        vm.SaveCommand.Execute(null);
        Assert(!vm.IsError && !vm.Dirty && File.Exists(ui.SavePath));
        var second = new WorkspaceViewModel(new FakeInteraction { OpenPath = ui.SavePath });
        second.OpenCommand.Execute(null);
        Assert(second.Current!.Spectra[0].Points.Count == 3 && second.Current.Scenarios.Count == 1 && !second.Dirty);
        ui.SavePath = Path.Combine(root, "report.html");
        vm.SelectedExport = WorkspaceViewModel.ExportOptions.Single(x => x.Key == "report");
        vm.ExportCommand.Execute(null);
        var report = File.ReadAllText(ui.SavePath);
        Assert(report.Contains("V2.0") && report.Contains("模拟") && report.Contains("SHA-256") && vm.LastOutput == ui.SavePath);
    });
    Check("无效草稿拒绝且有效输入和结果保留", () =>
    {
        var vm = New(new());
        var before = ProjectCodec.Serialize(vm.Current!);
        vm.Motor.PolePairs = "NaN";
        vm.ApplyCommand.Execute(null);
        Assert(vm.IsError && vm.HasAppliedDraftChanges && ProjectCodec.Serialize(vm.Current!) == before);
        Near(vm.FrequencyRows.Single(x => x.CandidateId == "electrical").FrequencyHz, 60);
    });
    Check("条件同步事件只触发一次且旧草稿已脱钩", () =>
    {
        var vm = New(new());
        var old = vm.Motor;
        vm.ApplyCommand.Execute(null);
        vm.ApplyCommand.Execute(null);
        var count = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.HasDraftChanges)) count++; };
        vm.Order.Name = "一次";
        Assert(count == 1);
        count = 0;
        old.PolePairs = "9";
        Assert(count == 0);
    });
    Check("新增表单保存阻断并提供具体提示", () =>
    {
        var path = Path.Combine(root, "unfinished.motorproj");
        var vm = New(new() { SavePath = path });
        vm.Order.Name = "未完成";
        vm.SaveCommand.Execute(null);
        Assert(vm.IsError && !File.Exists(path) && vm.Message.Contains("新增表单"));
    });
    Check("频谱预检后改变元信息必须重检", () =>
    {
        var vm = New(new());
        vm.Import.Name = "模拟";
        vm.Import.Reference = "任意参考";
        vm.Import.Condition = "测试";
        vm.Import.Csv = SpectrumCsv.Template();
        vm.PreviewCsvCommand.Execute(null);
        Assert(vm.Preview?.Success == true);
        vm.Import.Rpm = "2000";
        Assert(vm.Preview is null);
        vm.ImportCsvCommand.Execute(null);
        Assert(vm.IsError && vm.Current!.Spectra.Count == 0);
    });
    Check("CSV行错误整批拒绝且表中列出位置", () =>
    {
        var vm = New(new());
        vm.Import.Name = "坏谱";
        vm.Import.Reference = "测试";
        vm.Import.Condition = "测试";
        vm.Import.Csv = "frequency_hz,amplitude\n0,1\n1,bad\n";
        vm.PreviewCsvCommand.Execute(null);
        Assert(vm.IsError && vm.ImportIssues.Any(x => x.Path.Contains("line 3")) && vm.Current!.Spectra.Count == 0);
    });
    Check("关闭取消和保存对话框取消均保留当前工作", () =>
    {
        var ui = new FakeInteraction { Leave = LeaveChoice.Cancel };
        var vm = New(ui);
        Assert(!vm.TryClose() && vm.Current is not null);
        ui.Leave = LeaveChoice.Save;
        ui.SavePath = null;
        Assert(!vm.TryClose());
    });
    Check("未保存切换示例取消不改变项目", () =>
    {
        var ui = new FakeInteraction { Leave = LeaveChoice.Cancel };
        var vm = New(ui);
        var id = vm.Current!.Id;
        vm.DemoCommand.Execute(null);
        Assert(vm.Current.Id == id && !vm.Current.Demonstration);
    });
    Check("撤销重做恢复条件并保留审计", () =>
    {
        var vm = New(new());
        vm.Motor.PolePairs = "3";
        vm.ApplyCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert(vm.Current!.Motor.PolePairs == 2 && vm.Current.History.Any(x => x.Action == "撤销"));
        vm.RedoCommand.Execute(null);
        Assert(vm.Current.Motor.PolePairs == 3 && vm.Current.History.Any(x => x.Action == "重做"));
    });
    Check("归档暂停编辑并允许保存和取消归档", () =>
    {
        var vm = New(new());
        vm.ArchiveCommand.Execute(null);
        Assert(vm.Current!.Archived && !vm.ApplyCommand.CanExecute(null) && vm.SaveCommand.CanExecute(null));
        vm.ArchiveCommand.Execute(null);
        Assert(!vm.Current.Archived && vm.ApplyCommand.CanExecute(null));
    });
    Check("有刷配置不生成无刷候选并可扫查", () =>
    {
        var vm = New(new());
        vm.Motor.Topology = MotorTopology.BrushedPermanentMagnet;
        vm.ApplyCommand.Execute(null);
        Assert(vm.Candidates.All(x => x.Id != "electrical"));
        Near(vm.FrequencyRows.Single(x => x.CandidateId == "commutator").FrequencyHz, 300);
        vm.Sweep.Poles = "不适用";
        vm.SweepCommand.Execute(null);
        Assert(!vm.IsError && vm.SweepRows.Count == 6);
    });
    Check("扫查输入变更阻断旧结果导出", () =>
    {
        var path = Path.Combine(root, "stale.csv");
        var vm = New(new() { SavePath = path });
        vm.SweepCommand.Execute(null);
        Assert(vm.SweepRows.Count > 0);
        vm.Sweep.Frequency = "70";
        vm.SelectedExport = WorkspaceViewModel.ExportOptions.Single(x => x.Key == "sweep");
        vm.ExportCommand.Execute(null);
        Assert(vm.IsError && !File.Exists(path));
    });
    Check("所有成果类型实际写入并保留来源", () =>
    {
        var ui = new FakeInteraction();
        var vm = new WorkspaceViewModel(ui);
        vm.DemoCommand.Execute(null);
        vm.SweepCommand.Execute(null);
        foreach (var option in WorkspaceViewModel.ExportOptions)
        {
            ui.SavePath = Path.Combine(root, option.Key + option.Extension);
            vm.SelectedExport = option;
            vm.ExportCommand.Execute(null);
            Assert(!vm.IsError && new FileInfo(ui.SavePath).Length > 20, option.Key + ": " + vm.Message);
        }
    });
    Check("复制最大名称截断且新标识未归档", () =>
    {
        var vm = New(new());
        vm.Metadata.Name = new string('项', 100);
        vm.ApplyCommand.Execute(null);
        var old = vm.Current!.Id;
        while (vm.Current.History.Count < 2000)
        {
            vm.Current.History.Add(new AuditEvent { Action = "边界记录", Detail = "复制保持历史上限" });
        }
        vm.DuplicateCommand.Execute(null);
        Assert(!vm.IsError && vm.Current!.Id != old && vm.Current.Name.Length == 100 && vm.Current.History.Count == 2000);
    });
    Check("本机保存重开搬迁BOM与备份", () =>
    {
        var doc = new DocumentFile();
        var path = Path.Combine(root, "disk.motorproj");
        var project = Demonstration.Create();
        doc.Save(project, path, false);
        project.Name = "第二版";
        doc.Save(project, path, false);
        Assert(File.Exists(path + ".bak"));
        var backup = new DocumentFile().Open(path + ".bak");
        Assert(backup.Name != "第二版" && backup.Spectra[0].Points.Count == 751);
        var moved = Path.Combine(root, "moved.motorproj");
        File.WriteAllText(moved, ProjectCodec.Serialize(project), new UTF8Encoding(true));
        Assert(new DocumentFile().Open(moved).Name == "第二版");
    });
    Check("外部修改或删除拒绝覆盖并保留磁盘", () =>
    {
        var doc = new DocumentFile();
        var path = Path.Combine(root, "conflict.motorproj");
        doc.Save(new Project(), path, false);
        File.AppendAllText(path, " ");
        var changed = File.ReadAllText(path);
        Reject(() => doc.Save(new Project(), path, true));
        Assert(File.ReadAllText(path) == changed);
        File.Delete(path);
        Reject(() => doc.Save(new Project(), path, true));
    });
    Check("新路径已有文件未经确认拒绝", () =>
    {
        var path = Path.Combine(root, "exists.motorproj");
        File.WriteAllText(path, "original");
        Reject(() => new DocumentFile().Save(new Project(), path, false));
        Assert(File.ReadAllText(path) == "original");
    });
    Check("损坏UTF8与超大文件拒绝且会话不改变", () =>
    {
        var doc = new DocumentFile();
        var path = Path.Combine(root, "bad.motorproj");
        File.WriteAllBytes(path, [0xC3, 0x28]);
        Reject(() => doc.Open(path));
        Assert(doc.PathName is null);
        File.WriteAllBytes(path, new byte[ProjectCodec.MaximumFileBytes + 1]);
        Reject(() => doc.Open(path));
        Assert(doc.PathName is null);
    });
    Check("重复JSON字段与无效隐藏引用拒绝", () =>
    {
        var project = new Project();
        var text = ProjectCodec.Serialize(project).Replace("\"formatVersion\": 1,", "\"formatVersion\": 1,\"formatVersion\": 1,");
        Reject(() => ProjectCodec.Parse(text));
        project.Motor.HiddenCandidates.Add("custom:missing");
        Reject(() => ProjectValidator.ThrowIfInvalid(project));
    });
    Check("XAML绑定路径静态契约检查（不代替WPF运行）", () =>
    {
        var files = Directory.GetFiles("src/MotorFrequency.Desktop", "*.xaml", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        var inspected = 0;
        Type? ItemType(Type type) => type.GetInterfaces().Append(type)
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
        Type Resolve(Type context, string path, string file)
        {
            foreach (var part in path.Split('.'))
            {
                var property = context.GetProperty(part) ?? throw new Exception(file + ": unknown " + context.Name + "." + part);
                context = property.PropertyType;
            }
            return context;
        }
        void Visit(System.Xml.Linq.XElement node, Type context, Type? inheritedItems, string file)
        {
            Type? ownItems = null;
            foreach (var attribute in node.Attributes())
            {
                var match = System.Text.RegularExpressions.Regex.Match(attribute.Value, @"^\{Binding(?:\s+([^,}]+))?");
                if (match.Success && match.Groups[1].Success)
                {
                    var path = match.Groups[1].Value.Trim();
                    if (!path.Contains('='))
                    {
                        var type = Resolve(context, path, file);
                        inspected++;
                        if (attribute.Name.LocalName == "ItemsSource") ownItems = ItemType(type);
                    }
                }
                if (attribute.Name.LocalName == "ItemsSource" && attribute.Value.Contains("x:Static"))
                {
                    var key = attribute.Value.Split('.').Last().TrimEnd('}');
                    ownItems = ItemType(typeof(WorkspaceViewModel).GetProperty(key)!.PropertyType);
                }
            }
            foreach (var child in node.Elements())
            {
                var itemChild = child.Name.LocalName.EndsWith(".Columns") || child.Name.LocalName.EndsWith(".ItemTemplate");
                Visit(child, itemChild ? ownItems ?? inheritedItems ?? context : context, ownItems ?? inheritedItems, file);
            }
        }
        foreach (var file in files)
        {
            Visit(System.Xml.Linq.XDocument.Load(file).Root!, typeof(WorkspaceViewModel), null, file);
        }
        Assert(inspected > 150, "Expected substantial native binding contract coverage");
    });
    Check("溢出计算拒绝而合法零速为零", () =>
    {
        Reject(() => FrequencyEngine.At(double.MaxValue, double.MaxValue));
        Near(FrequencyEngine.At(10000, 0), 0);
    });
}
finally
{
    Directory.Delete(root, true);
}
Directory.CreateDirectory("docs/sample-exports");
var sampleUi = new FakeInteraction();
var sample = new WorkspaceViewModel(sampleUi);
sample.DemoCommand.Execute(null);
sample.SweepCommand.Execute(null);
foreach (var option in WorkspaceViewModel.ExportOptions)
{
    sampleUi.SavePath = Path.GetFullPath("docs/sample-exports/模拟-" + option.Key + option.Extension);
    sample.SelectedExport = option;
    sample.ExportCommand.Execute(null);
    if (sample.IsError) throw new InvalidOperationException(sample.Message);
}
sampleUi.SavePath = Path.GetFullPath("docs/sample-exports/独立模拟案例.motorproj");
sample.SaveCommand.Execute(null);
if (sample.IsError) throw new InvalidOperationException(sample.Message);
var evidence = new { at = DateTimeOffset.UtcNow, os = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(),
    passed = results.Count - failed, failed, scope = "Cross-platform application MVVM and native-file semantics; no WPF window executed", checks = results };
Directory.CreateDirectory("docs");
File.WriteAllText("docs/application-evidence.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"TOTAL {results.Count}; PASS {results.Count - failed}; FAIL {failed}");
return failed == 0 ? 0 : 1;

sealed class FakeInteraction : IDesktopInteraction
{
    public string? OpenPath { get; set; }
    public string? SavePath { get; set; }
    public string? CsvPath { get; set; }
    public LeaveChoice Leave { get; set; } = LeaveChoice.Discard;
    public bool ConfirmResult { get; set; } = true;
    public string? OpenProjectFile() => OpenPath;
    public string? OpenSpectrumFile() => CsvPath;
    public string? SaveFile(string suggestedName, string extension, string description) => SavePath;
    public LeaveChoice ConfirmLeave(string reason) => Leave;
    public bool Confirm(string message) => ConfirmResult;
    public void RevealFile(string path) { }
}
