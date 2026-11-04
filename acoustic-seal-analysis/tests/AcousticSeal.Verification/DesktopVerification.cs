using System.Text;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.DesktopLogic;

internal static class DesktopVerification
{
    public static int Run()
    {
        var failed = 0;
        var tests = new (string, Action)[]
        {
            ("原生编辑工作流：空白录入、配对、保存重开、导出", CompleteWorkflow),
            ("原生编辑：错误字段不改变已应用项目", InvalidDraft),
            ("原生导航：取消切换保留草稿，放弃后恢复输入", NavigationGuard),
            ("原生关闭：取消保存阻止关闭，保存后可重开", CloseGuard),
            ("原生文件：冲突、删除冲突、覆盖保护与备份", NativeFileConflict),
            ("原生文件：备份打开后必须另存", BackupOpen),
            ("原生CSV：预检后修改内容必须重检", CsvPreviewGuard),
            ("原生频带草稿：重复、非有限、缺失与边界", DraftBoundaries),
            ("原生复制：项目副本与试验结构命令不混用", CopyCommands),
            ("原始核对：无显式配对时不自动选择试验", WorkbookSelection)
        };
        foreach (var (name, action) in tests)
        {
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + " " + error); }
        }
        Console.WriteLine($"DESKTOP_LOGIC_RESULT: passed={tests.Length - failed}; failed={failed}; UI_EXECUTED=false");
        return failed;
    }
    private static void Assert(bool value, string message = "assert failed")
    {
        if (!value) throw new Exception(message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("expected " + typeof(T).Name);
    }
    private static string Folder()
    {
        var path = Path.Combine(Path.GetTempPath(), "seal-native-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void Select(Workspace vm, string key) => vm.SelectedModule = vm.Modules.Single(item => item.Key == key);
    private static void Field(Workspace vm, string key, string value) => vm.Editor!.Fields.Single(item => item.Key == key).Value = value;
    private static void Do(Workspace vm, FakeDialogs dialogs, ActionCommand command)
    {
        command.Execute(null);
        Assert(dialogs.Errors.Count == 0, string.Join(";", dialogs.Errors));
    }
    private static void CompleteWorkflow()
    {
        var root = Folder();
        try
        {
            var ui = new FakeDialogs(); var vm = new Workspace(ui);
            Assert(vm.SourceSummary == "尚无试验数据" && vm.Results.Count == 0);
            Field(vm, "Name", "手工模拟封堵案例"); Field(vm, "Owner", "验证人员");
            Do(vm, ui, vm.ApplyCommand);
            Select(vm, "location"); Do(vm, ui, vm.AddCommand);
            Field(vm, "Name", "车门密封条"); Field(vm, "Material", "模拟胶带");
            Do(vm, ui, vm.ApplyCommand);
            Select(vm, "condition"); Do(vm, ui, vm.AddCommand);
            foreach (var (key, value) in new[] { ("Name", "模拟工况"), ("Vehicle", "SIM-01"), ("Microphone", "驾驶员耳旁"), ("Excitation", "模拟声源"), ("Surface", "模拟台架"), ("Gear", "N"), ("Instrument", "SIM-MIC"), ("Calibration", "模拟链路"), ("Speed", "0"), ("Rpm", "0"), ("Temperature", "20"), ("Duration", "10") }) Field(vm, key, value);
            Do(vm, ui, vm.ApplyCommand);
            Select(vm, "trial"); Do(vm, ui, vm.AddCommand);
            Field(vm, "Name", "基线"); Field(vm, "SourceNote", "人工填写的独立流程验证数值");
            vm.Editor!.AddBand(new BandDraft { Frequency = "125", Level = "70" });
            Do(vm, ui, vm.ApplyCommand);
            var baseline = vm.Editor!.Identity;
            Do(vm, ui, vm.AddCommand);
            Field(vm, "Name", "单项封堵"); Field(vm, "Kind", "Single"); Field(vm, "SourceNote", "人工填写的独立流程验证数值");
            vm.Editor!.Locations[0].Selected = true;
            vm.Editor.AddBand(new BandDraft { Frequency = "125", Level = "67" });
            Do(vm, ui, vm.ApplyCommand);
            var target = vm.Editor!.Identity;
            Select(vm, "pair"); Do(vm, ui, vm.AddCommand);
            Field(vm, "BaselineId", baseline); Field(vm, "TargetId", target);
            Do(vm, ui, vm.ApplyCommand);
            Assert(vm.Results.Single().Bands.Single().Difference == 3, vm.ResultNote);
            var projectPath = Path.Combine(root, "project.sealproj");
            ui.Saves.Enqueue(projectPath); Do(vm, ui, vm.SaveCommand);
            Assert(!vm.HasChanges && File.Exists(projectPath));
            var reopened = new Workspace(ui); ui.Opens.Enqueue(projectPath); Do(reopened, ui, reopened.OpenCommand);
            Assert(reopened.Results.Single().Bands.Single().Difference == 3);
            ui.Saves.Enqueue(Path.Combine(root, "result.csv")); Do(reopened, ui, reopened.ResultsCommand);
            Assert(File.ReadAllText(Path.Combine(root, "result.csv")).Contains("3.0.0"));
            ui.Saves.Enqueue(Path.Combine(root, "report.html")); Do(reopened, ui, reopened.ReportCommand);
            Assert(File.ReadAllText(Path.Combine(root, "report.html")).Contains("人工填写的独立流程验证数值"));
            Do(reopened, ui, reopened.DemoCommand);
            Assert(reopened.SourceSummary.Contains("模拟") && reopened.Results.Count == 6 && reopened.FilePath == "尚未保存到磁盘");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void InvalidDraft()
    {
        var ui = new FakeDialogs(); var vm = new Workspace(ui);
        Field(vm, "Name", ""); vm.ApplyCommand.Execute(null);
        Assert(ui.Errors.Count == 1 && vm.Editor!.Dirty && vm.Title.Contains("未命名"));
        Field(vm, "Name", "修复项目"); ui.Errors.Clear(); Do(vm, ui, vm.ApplyCommand);
        Assert(vm.Title.Contains("修复项目") && !vm.Editor!.Dirty);
    }
    private static void NavigationGuard()
    {
        var ui = new FakeDialogs(); var vm = new Workspace(ui);
        Field(vm, "Name", "尚未应用"); ui.Decisions.Enqueue(null); Select(vm, "location");
        Assert(vm.SelectedModule!.Key == "project" && vm.Editor!.Fields[0].Value == "尚未应用");
        ui.Decisions.Enqueue(false); Select(vm, "location");
        Select(vm, "project"); Assert(vm.Editor!.Fields[0].Value == "未命名声学试验项目");
        Field(vm, "Name", ""); ui.Decisions.Enqueue(true); Select(vm, "location");
        Assert(vm.SelectedModule!.Key == "project" && ui.Errors.Count == 1);
    }
    private static void CloseGuard()
    {
        var root = Folder();
        try
        {
            var ui = new FakeDialogs(); var vm = new Workspace(ui);
            Field(vm, "Name", "关闭保存"); Do(vm, ui, vm.ApplyCommand);
            ui.Decisions.Enqueue(null); Assert(!vm.CanClose());
            ui.Decisions.Enqueue(true); Assert(!vm.CanClose()); // cancelled native SaveFile dialog
            ui.Decisions.Enqueue(true); ui.Saves.Enqueue(Path.Combine(root, "close.sealproj"));
            Assert(vm.CanClose() && File.Exists(Path.Combine(root, "close.sealproj")));
        }
        finally { Directory.Delete(root, true); }
    }
    private static void NativeFileConflict()
    {
        var root = Folder();
        try
        {
            var path = Path.Combine(root, "p.sealproj"); var io = new DesktopFiles(); var p = AcousticProject.Empty();
            io.Save(p, path, false);
            p.Name = "second"; io.Save(p, path, false);
            Assert(new ProjectCodec().Decode(File.ReadAllBytes(path + ".bak")).Project.Name != "second");
            File.AppendAllText(path, " "); Throws<ProjectConflictException>(() => io.Save(p, path, false));
            io.Open(path); File.Delete(path); Throws<ProjectConflictException>(() => io.Save(p, path, false));
            File.WriteAllBytes(path, new ProjectCodec().Encode(p));
            Throws<IOException>(() => new DesktopFiles().Save(p, path, false));
            new DesktopFiles().Save(p, path, true);
            Assert(File.Exists(path + ".bak"));
        }
        finally { Directory.Delete(root, true); }
    }
    private static void BackupOpen()
    {
        var root = Folder();
        try
        {
            var path = Path.Combine(root, "old.sealproj.bak");
            File.WriteAllBytes(path, new ProjectCodec().Encode(AcousticProject.Empty()));
            var io = new DesktopFiles(); io.Open(path);
            Assert(io.Path == "" && io.OpenNotes.Contains("另存"));
        }
        finally { Directory.Delete(root, true); }
    }
    private static void CsvPreviewGuard()
    {
        var ui = new FakeDialogs(); var vm = new Workspace(ui); Do(vm, ui, vm.DemoCommand);
        Select(vm, "import");
        vm.ImportText = $"trial_id,frequency_hz,level_db\\n{Demonstration.StableId(30)},125,71".Replace("\\n", "\n");
        Do(vm, ui, vm.PreviewCommand); Assert(vm.ImportStatus.Contains("可以应用"));
        vm.ImportText += "\n"; vm.ImportCommand.Execute(null);
        Assert(ui.Errors.Count == 1 && ui.Errors[0].Contains("先预检"));
    }
    private static void DraftBoundaries()
    {
        var p = Demonstration.Create(); var editor = new EntityEditor(ProjectCodec.Clone(p.Trials[0]), p);
        editor.Paste("125,0\n250,\n500,-100");
        var trial = (AcousticTrial)editor.Read();
        Assert(trial.Bands[0].Level == 0 && trial.Bands[1].Level is null && trial.Bands[2].Level == -100);
        Throws<DomainValidationException>(() => editor.Paste("125,60\n125,61"));
        Assert(editor.Bands.Count == 3);
        Throws<DomainValidationException>(() => editor.Paste("125,NaN"));
        editor.Bands[0].Frequency = "NaN";
        Throws<DomainValidationException>(() => editor.Read());
    }
    private static void CopyCommands()
    {
        var root = Folder();
        try
        {
            var ui = new FakeDialogs(); var vm = new Workspace(ui); Do(vm, ui, vm.DemoCommand);
            Select(vm, "trial");
            var count = vm.Items.Count;
            Do(vm, ui, vm.CopyCommand);
            Assert(vm.Items.Count == count, "File menu project copy must not duplicate a trial");
            var path = Path.Combine(root, "copy.sealproj"); ui.Saves.Enqueue(path); Do(vm, ui, vm.SaveCommand);
            Assert(new ProjectCodec().Decode(File.ReadAllBytes(path)).Project.Id != Demonstration.Create().Id);
            Do(vm, ui, vm.DuplicateTrialCommand);
            Assert(vm.Items.Count == count + 1 && vm.Editor!.Bands.Count == 0);
        }
        finally { Directory.Delete(root, true); }
    }
    private static void WorkbookSelection()
    {
        var ui = new FakeDialogs(); var vm = new Workspace(ui);
        vm.WorkbookCommand.Execute(null);
        Assert(ui.Errors.Count == 1 && ui.Errors[0].Contains("显式配对") && ui.Saves.Count == 0);
    }
}

internal sealed class FakeDialogs : IUserDialogs
{
    public Queue<string> Opens { get; } = [];
    public Queue<string> Saves { get; } = [];
    public Queue<bool?> Decisions { get; } = [];
    public List<string> Errors { get; } = [];
    public string? OpenFile(string title, string filter) => Opens.TryDequeue(out var path) ? path : null;
    public string? SaveFile(string title, string filter, string filename) => Saves.TryDequeue(out var path) ? path : null;
    public bool Confirm(string title, string text) => true;
    public bool? SaveChanges(string text) => Decisions.TryDequeue(out var answer) ? answer : false;
    public void ShowError(string text) => Errors.Add(text);
}
