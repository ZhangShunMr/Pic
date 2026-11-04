using System.Text.Json;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Desktop.Services;
using Blower.Desktop.ViewModels;

var checks = new List<object>();
var failures = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Test(string name, Action action)
{
    try
    {
        action();
        checks.Add(new { name, status = "passed" });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failures++;
        checks.Add(new { name, status = "failed", error = e.Message });
        Console.WriteLine("FAIL " + name + ": " + e.Message);
    }
}
void Select(WorkspaceViewModel vm, string key)
{
    vm.SelectedNode = vm.Nodes.Single(x => x.Key == key);
}
void Field(WorkspaceViewModel vm, string label, string value)
{
    vm.Form.Fields.Single(x => x.Label == label).Value = value;
}
var root = Path.Combine(Path.GetTempPath(), "blower-native-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var dialogs = new FakeDialogs { NextPath = Path.Combine(root, "project.blowerproj") };
var vm = new WorkspaceViewModel(dialogs);
Test("startup is blank and dirty", () =>
{
    Assert(vm.Project.Plans.Count == 0 && vm.Project.Modes.Count == 0, "not blank");
    Assert(vm.Dirty && vm.Result == null, "unexpected state");
});
Test("invalid editor is transactional and stays editable", () =>
{
    Select(vm, "settings");
    Field(vm, "叶片数", "0");
    vm.ApplyCommand.Execute(null);
    Assert(vm.Error && vm.Project.Settings.Blades == 40 && vm.Form.Changed, "invalid editor accepted");
    vm.ResetEditorCommand.Execute(null);
    Assert(!vm.Form.Changed, "reset failed");
});
Test("invalid numeric text points to field", () =>
{
    Field(vm, "扫描步长 rpm", "NaN");
    vm.ApplyCommand.Execute(null);
    Assert(vm.Error && vm.Notice.Contains("扫描步长"), "missing field error");
    vm.ResetEditorCommand.Execute(null);
});
Test("valid editor applies and history restores", () =>
{
    Field(vm, "叶片数", "41");
    vm.ApplyCommand.Execute(null);
    Assert(vm.Project.Settings.Blades == 41, "apply failed");
    vm.UndoCommand.Execute(null);
    Assert(vm.Project.Settings.Blades == 40, "undo failed");
    vm.RedoCommand.Execute(null);
    Assert(vm.Project.Settings.Blades == 41, "redo failed");
});
Test("blank save reopen uses real file and hash", () =>
{
    vm.SaveCommand.Execute(null);
    Assert(!vm.Dirty && File.Exists(dialogs.NextPath), "save failed");
    var other = new WorkspaceViewModel(dialogs);
    other.OpenCommand.Execute(null);
    Assert(!other.Dirty && other.Project.Settings.Blades == 41, "reopen failed");
});
Test("demo is explicit and independent", () =>
{
    vm.DemoCommand.Execute(null);
    Assert(vm.Project.Origin == Origin.Simulation && vm.Project.Plans.Count == 2, "demo failed");
    Assert(vm.FilePath.Contains("尚未保存"), "demo retained disk path");
});
Test("read only locked plan refuses gear changes", () =>
{
    Select(vm, "plans");
    Field(vm, "锁定方案", "是");
    vm.ApplyCommand.Execute(null);
    var rpm = vm.ActivePlan!.Gears[0].Rpm;
    vm.SelectedRow = vm.ActivePlan.Gears[0];
    Field(vm, "转速 rpm", (rpm + 1).ToString());
    vm.ApplyCommand.Execute(null);
    Assert(vm.Error && vm.ActivePlan.Gears[0].Rpm == rpm, "locked edit accepted");
    vm.ResetEditorCommand.Execute(null);
});
Test("CSV preview rejects bad data without mutation", () =>
{
    Select(vm, "exchange");
    var hash = ProjectCodec.InputHash(vm.Project);
    vm.CsvText = "name,rpm,fixed\nlow,bad,false";
    vm.PreviewCommand.Execute(null);
    vm.ImportApplyCommand.Execute(null);
    Assert(vm.Error && ProjectCodec.InputHash(vm.Project) == hash, "invalid CSV changed project");
});
Test("CSV preview apply creates a separate plan", () =>
{
    vm.CsvText = "name,rpm,fixed\nlow,900,false\nhigh,1800,true";
    vm.PreviewCommand.Execute(null);
    vm.ImportApplyCommand.Execute(null);
    Assert(vm.Project.Plans.Count == 3 && vm.Project.Plans[^1].Gears.Count == 2, "CSV apply failed");
    Assert(vm.Project.Plans[^1].Gears[1].Fixed, "fixed flag lost");
});
Test("edited CSV invalidates preview", () =>
{
    vm.PreviewCommand.Execute(null);
    vm.CsvText += "\nlast,3000,false";
    vm.ImportApplyCommand.Execute(null);
    Assert(vm.Error && vm.Project.Plans.Count == 3, "stale preview accepted");
});
Test("atomic desktop save keeps backup and detects conflicts", () =>
{
    var path = Path.Combine(root, "atomic.blowerproj");
    var p = Demo.Create();
    var token = DesktopFiles.Save(p, path, null);
    p.Name = "revision 2";
    var next = DesktopFiles.Save(p, path, token);
    Assert(DesktopFiles.Open(path + ".bak").Project.Name != p.Name, "backup missing");
    File.AppendAllText(path, " ");
    try { DesktopFiles.Save(p, path, next); throw new Exception("conflict accepted"); }
    catch (IOException) { }
});
Test("completed evidence and archive validation at editor boundary", () =>
{
    vm.ArchiveCommand.Execute(null);
    Assert(vm.Project.Archived, "archive failed");
    var count = vm.Project.Plans.Count;
    vm.ImportApplyCommand.Execute(null);
    Assert(vm.Project.Plans.Count == count, "archive mutated");
    vm.ArchiveCommand.Execute(null);
    Assert(!vm.Project.Archived, "unarchive failed");
});
Test("unsaved close cancel and discard", () =>
{
    dialogs.Decision = SaveDecision.Cancel;
    Assert(!vm.CanClose(), "cancel ignored");
    dialogs.Decision = SaveDecision.Discard;
    Assert(vm.CanClose(), "discard blocked");
});
Test("blank native workflow through compute scan compare save reopen export", () =>
{
    var d = new FakeDialogs {NextPath=Path.Combine(root,"complete.blowerproj"), ExportFile=Path.Combine(root,"complete.csv")};
    var w = new WorkspaceViewModel(d);
    Select(w,"orders"); w.AddCommand.Execute(null); w.SelectedRow=w.Project.Orders[0];
    Field(w,"阶次类型",Labels.Kind(OrderKind.Blade)); w.ApplyCommand.Execute(null);
    Select(w,"modes"); w.AddCommand.Execute(null); w.SelectedRow=w.Project.Modes[0];
    Field(w,"模态频率 Hz","600"); Field(w,"阻尼比 ζ","0.02"); w.ApplyCommand.Execute(null);
    Select(w,"plans"); w.AddPlanCommand.Execute(null); w.AddGearCommand.Execute(null);
    w.SelectedRow=w.ActivePlan!.Gears[0]; Field(w,"转速 rpm","900"); w.ApplyCommand.Execute(null);
    w.ComputeCommand.Execute(null);
    Assert(SpinWait.SpinUntil(()=>!w.Busy,10000),"compute timed out");
    Assert(w.Result?.Contacts.Single().Near==true,"expected contact missing");
    w.ExportContactsCommand.Execute(null);
    Assert(File.ReadAllText(d.ExportFile).Contains("600"),"export did not contain frequency");
    w.ScanCommand.Execute(null);
    Assert(SpinWait.SpinUntil(()=>!w.Busy,10000) && w.Scan?.Feasible==true,"scan failed");
    w.AcceptScanCommand.Execute(null);
    Assert(w.Project.Plans.Count==2,"scan did not create new plan");
    w.ComputeCommand.Execute(null); Assert(SpinWait.SpinUntil(()=>!w.Busy,10000),"recompute timed out");
    w.ComparePlan=w.Project.Plans[1]; w.CompareCommand.Execute(null);
    Assert(w.Comparison?.Rows.Count==1,"comparison missing");
    w.GenerateChecksCommand.Execute(null); Assert(w.Project.Checks.Count==1,"verification task missing");
    w.SaveCommand.Execute(null); Assert(!w.Dirty,"save failed");
    var reopened=DesktopFiles.Open(d.NextPath).Project;
    Assert(reopened.Plans.Count==2 && reopened.Checks.Count==1,"reopen lost inputs");
});
File.WriteAllText(args.FirstOrDefault() ?? "workflow-evidence.json",
    JsonSerializer.Serialize(new { date = DateTimeOffset.UtcNow, platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription + "; linked production ViewModels and file service; this test does not instantiate WPF", passed = checks.Count - failures, failed = failures, checks }, new JsonSerializerOptions { WriteIndented = true }));
Directory.Delete(root, true);
Console.WriteLine($"{checks.Count - failures}/{checks.Count} passed");
return failures == 0 ? 0 : 1;

sealed class FakeDialogs : IDesktopDialogs
{
    public string NextPath { get; set; } = "";
    public string? ExportFile { get; set; }
    public SaveDecision Decision { get; set; } = SaveDecision.Discard;
    public bool Confirm(string message) => true;
    public SaveDecision Unsaved(string message) => Decision;
    public string? OpenProject() => NextPath;
    public string? SaveProject(string name) => NextPath;
    public string? OpenCsv() => null;
    public string? ExportPath(string name, string extension) => ExportFile;
}
