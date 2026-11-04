using System.Text.Json;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Desktop.ViewModels;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var directory = Path.Combine(Path.GetTempPath(), "trim-desktop-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var checks = new List<object>();
var failures = 0;
void Assert(bool value, string message = "assertion failed")
{
    if (!value) throw new Exception(message);
}
void Check(string name, Action action)
{
    try { action(); checks.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; checks.Add(new { name, passed = false, error = e.Message }); Console.WriteLine("FAIL " + name + " " + e.Message); }
}
async Task CheckAsync(string name, Func<Task> action)
{
    try { await action(); checks.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; checks.Add(new { name, passed = false, error = e.Message }); Console.WriteLine("FAIL " + name + " " + e.Message); }
}
(WorkspaceViewModel vm, FakeInteraction ui) Demo()
{
    var ui = new FakeInteraction();
    var vm = new WorkspaceViewModel(ui);
    vm.DemoCommand.Execute(null);
    return (vm, ui);
}
async Task Finish(WorkspaceViewModel vm)
{
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (vm.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(10);
    Assert(!vm.IsBusy, "background command timeout");
}

Check("ordinary startup blank and demo explicit", () =>
{
    using var vm = new WorkspaceViewModel(new FakeInteraction());
    Assert(vm.Draft.Variants.Count == 0 && vm.Draft.Loads.Count == 0 && !vm.HasUnsavedChanges);
    vm.DemoCommand.Execute(null);
    Assert(vm.Draft.Origin == DataOrigin.Simulation && vm.HasUnsavedChanges);
});
Check("draft validation rolls back applied input", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.Draft.Panel.MassKg = 0;
    vm.ApplyCommand.Execute(null);
    Assert(vm.Applied.Panel.MassKg == 1 && vm.Draft.Panel.MassKg == 0 && ui.Errors.Count == 1);
});
Check("nonfinite draft remains inspectable and rejected", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.Draft.Panel.MassKg = double.NaN;
    vm.NotifyDraftEdited();
    Assert(vm.DraftDirty);
    vm.ApplyCommand.Execute(null);
    Assert(ui.Errors.Count == 1 && vm.Applied.Panel.MassKg == 1);
});
Check("binding conversion errors block saving and calculation", () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.SetBindingErrors(1);
    Assert(!vm.SaveCommand.CanExecute(null) && !vm.CalculateCommand.CanExecute(null));
    vm.DiscardDraftCommand.Execute(null);
    Assert(!vm.HasConversionErrors);
});
Check("draft apply undo redo", () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.Draft.Panel.MassKg = 2; vm.ApplyCommand.Execute(null);
    Assert(vm.Applied.Panel.MassKg == 2);
    vm.UndoCommand.Execute(null); Assert(vm.Applied.Panel.MassKg == 1);
    vm.RedoCommand.Execute(null); Assert(vm.Applied.Panel.MassKg == 2);
});
Check("new rectangle blank workflow supports collections", () =>
{
    using var vm = new WorkspaceViewModel(new FakeInteraction());
    vm.RectangleCommand.Execute(null); vm.AddVariantCommand.Execute(null);
    foreach (var point in new[] { (40,40), (360,40), (200,260) })
    {
        vm.PlaceAt(point.Item1, point.Item2, null); vm.AddClipCommand.Execute(null);
    }
    vm.AddLoadCommand.Execute(null); vm.ApplyCommand.Execute(null);
    Assert(vm.Clips.Count == 3 && vm.Variants.Count == 1 && vm.Loads.Count == 1);
    ProjectValidation.Require(vm.Applied, true);
});
Check("selected point clears on new blank project", () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.NewCommand.Execute(null);
    Assert(vm.SelectedContact is null && vm.SelectedClip is null && vm.SelectedVariant is null);
});
Check("save reopen preserves applied model and source", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    var path = Path.Combine(directory, "roundtrip.trimproj"); ui.Save = path;
    vm.SaveCommand.Execute(null);
    var hash = ProjectCodec.InputHash(vm.Applied);
    Assert(!vm.HasUnsavedChanges && File.Exists(path));
    vm.NewCommand.Execute(null); ui.Open = path; vm.OpenCommand.Execute(null);
    Assert(ProjectCodec.InputHash(vm.Applied) == hash && !vm.ResultsCurrent && vm.FileLocation == path);
});
Check("native file overwrite backup and external change protection", () =>
{
    var p = Demonstration.Create(); var path = Path.Combine(directory, "file.trimproj");
    var first = DesktopFiles.Save(path, p, null); p.Name = "next";
    var second = DesktopFiles.Save(path, p, first.Hash);
    Assert(DesktopFiles.Open(path + ".bak").Project.Name != "next");
    File.AppendAllText(path, "\n");
    var rejected = false;
    try { DesktopFiles.Save(path, p, second.Hash); } catch (InvalidDataException) { rejected = true; }
    Assert(rejected);
});
Check("missing native file and corrupt native JSON rejected", () =>
{
    var path = Path.Combine(directory, "bad.trimproj"); File.WriteAllText(path, "{}");
    bool rejected = false;
    try { DesktopFiles.Open(path); } catch (InvalidDataException) { rejected = true; }
    Assert(rejected);
});
Check("cancel save dialog prevents close", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    ui.Choice = UnsavedChoice.Save; ui.Save = null;
    Assert(!vm.RequestClose());
});
Check("cancel unsaved prompt preserves project", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    var id = vm.Draft.Id; ui.Choice = UnsavedChoice.Cancel;
    vm.NewCommand.Execute(null); Assert(vm.Draft.Id == id);
});
Check("invalid draft cannot be silently saved before closing", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.Draft.Panel.MassKg = 0; ui.Choice = UnsavedChoice.Save; ui.Save = Path.Combine(directory, "invalid.trimproj");
    Assert(!vm.RequestClose() && ui.Errors.Count == 1 && !File.Exists(ui.Save));
});
Check("archive read only and copied project editable", () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.ArchiveCommand.Execute(null); var id = vm.Applied.Id;
    Assert(!vm.CanEdit && !vm.AddContactCommand.CanExecute(null));
    vm.CopyProjectCommand.Execute(null); Assert(vm.CanEdit && vm.Applied.Id != id && vm.HasUnsavedChanges);
});
Check("copy and delete scheme maintain independent identity", () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    var id = vm.SelectedVariant!.Contacts[0].Id;
    vm.CopyVariantCommand.Execute(null); Assert(vm.Variants.Count == 2 && vm.SelectedVariant!.Contacts[0].Id != id);
    vm.DeleteVariantCommand.Execute(null); Assert(vm.Variants.Count == 1);
});
Check("CSV preview applies transaction and undo restores", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.ImportCsvText = LayoutCsv.Template; vm.ImportReplace = true;
    vm.PreviewImportCommand.Execute(null); Assert(vm.ImportMessages.Count > 0);
    var id = vm.SelectedContact!.Id; vm.ApplyImportCommand.Execute(null);
    Assert(vm.SelectedContact!.Id != id && ui.Errors.Count == 0);
    vm.UndoCommand.Execute(null); Assert(vm.SelectedContact!.Id == id);
});
Check("CSV preview text edit cannot apply stale content", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.ImportCsvText = LayoutCsv.Template; vm.ImportReplace = true; vm.PreviewImportCommand.Execute(null);
    vm.ImportCsvText += "\n"; vm.ApplyImportCommand.Execute(null);
    Assert(ui.Errors.Count == 1);
});
Check("CSV target change blocked after preview", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.CopyVariantCommand.Execute(null); vm.ApplyCommand.Execute(null);
    vm.ImportCsvText = LayoutCsv.Template; vm.ImportReplace = true; vm.PreviewImportCommand.Execute(null);
    vm.SelectedVariant = vm.Variants[0]; vm.ApplyImportCommand.Execute(null);
    Assert(ui.Errors.Count == 1);
});
Check("batch request changes blocked after preview", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.BatchRequest.PreloadDeltaMm = 0.1; vm.PreviewBatchCommand.Execute(null);
    vm.BatchRequest.PreloadDeltaMm = 0.2; vm.ApplyBatchCommand.Execute(null);
    Assert(ui.Errors.Count == 1 && vm.Applied.Variants[0].Contacts[0].PreloadMm == 0.2);
});
Check("batch updates and undo", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.BatchRequest.PreloadDeltaMm = 0.1; vm.PreviewBatchCommand.Execute(null); vm.ApplyBatchCommand.Execute(null);
    Assert(Math.Abs(vm.SelectedContact!.PreloadMm - 0.3) < 1e-10 && ui.Errors.Count == 0);
    vm.UndoCommand.Execute(null); Assert(vm.SelectedContact!.PreloadMm == 0.2);
});
await CheckAsync("calculate valid results and dirty edit hides old output", async () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.CalculateCommand.Execute(null); await Finish(vm);
    Assert(vm.ResultsCurrent && vm.Envelopes.Count == 5 && ui.Errors.Count == 0);
    vm.Draft.Panel.MassKg = 2; vm.NotifyDraftEdited();
    Assert(!vm.ResultsCurrent && vm.Envelopes.Count == 0 && !vm.ExportCsvCommand.CanExecute(null));
    vm.ApplyCommand.Execute(null); Assert(!vm.ResultsCurrent);
    vm.UndoCommand.Execute(null); Assert(vm.ResultsCurrent);
});
await CheckAsync("valid result exports include actual source hash and units", async () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.CalculateCommand.Execute(null); await Finish(vm);
    ui.Save = Path.Combine(directory, "envelope.csv"); vm.ExportCsvCommand.Execute(null);
    var text = File.ReadAllText(ui.Save);
    Assert(text.Contains(ProjectCodec.InputHash(vm.Applied)) && text.Contains("模拟演示") && text.Contains("mm"));
    Assert(vm.BalanceRows.All(r => Math.Abs(r.NormalErrorN) < 1e-6));
});
await CheckAsync("generated tasks preserve result freshness", async () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.CalculateCommand.Execute(null); await Finish(vm); vm.GenerateTasksCommand.Execute(null);
    Assert(vm.Tasks.Count > 1 && vm.ResultsCurrent);
});
await CheckAsync("scan preserves base design and invalidates with input edits", async () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    var before = ProjectCodec.InputHash(vm.Applied);
    vm.SweepSteps = 4; vm.SweepCommand.Execute(null); await Finish(vm);
    Assert(vm.SweepCurrent && vm.SweepRows.Count == 4 && ProjectCodec.InputHash(vm.Applied) == before && ui.Errors.Count == 0);
    vm.Draft.Panel.MassKg = 2; vm.NotifyDraftEdited(); Assert(!vm.SweepCurrent && vm.SweepRows.Count == 0);
});
await CheckAsync("invalid scan reports error without partial output", async () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.SweepTo = -1; vm.SweepCommand.Execute(null); await Finish(vm);
    Assert(ui.Errors.Count == 1 && !vm.SweepCurrent);
});
await CheckAsync("inflight close and cancellation", async () =>
{
    var (vm, _) = Demo(); using var owned = vm;
    vm.SweepSteps = 15; vm.SweepCommand.Execute(null);
    Assert(vm.IsBusy && !vm.RequestClose()); vm.CancelCommand.Execute(null); await Finish(vm);
    Assert(!vm.SweepCurrent);
});
Check("reference compare detects shared condition mismatch", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    var p = ProjectCodec.Clone(vm.Applied); p.Panel.MassKg = 2;
    ui.Open = Path.Combine(directory, "reference.trimproj"); DesktopFiles.Save(ui.Open, p, null);
    vm.CompareFileCommand.Execute(null); Assert(vm.Differences.Count == 1 && vm.DifferenceSummary.Contains("不同"));
});
Check("native library store open and backup recovery copies identity", () =>
{
    var ui = new FakeInteraction();
    using var vm = new WorkspaceViewModel(ui, Path.Combine(directory, "native-library"));
    vm.DemoCommand.Execute(null); vm.SaveLibraryCommand.Execute(null);
    Assert(vm.LibraryRows.Count == 1 && !vm.HasUnsavedChanges);
    var originalId = vm.Applied.Id;
    vm.Draft.Name = "second revision"; vm.ApplyCommand.Execute(null); vm.SaveLibraryCommand.Execute(null);
    Assert(vm.LibraryRows.Count == 1);
    vm.RecoverLibraryCommand.Execute(null);
    Assert(vm.Applied.Id != originalId && vm.Applied.Name.Contains("副本") && vm.HasUnsavedChanges);
    vm.SaveLibraryCommand.Execute(null); Assert(vm.LibraryRows.Count == 2 && ui.Errors.Count == 0);
});
Check("native library corrupt item remains visible for recovery", () =>
{
    var ui = new FakeInteraction(); var path = Path.Combine(directory, "bad-library"); Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path, Guid.NewGuid() + ".trimproj"), "bad");
    using var vm = new WorkspaceViewModel(ui, path); vm.RefreshLibraryCommand.Execute(null);
    Assert(vm.LibraryRows.Count == 1 && vm.LibraryRows[0].Error is not null && !vm.OpenLibraryCommand.CanExecute(null));
});
Check("batch target switch requires a fresh preview", () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.CopyVariantCommand.Execute(null); vm.ApplyCommand.Execute(null);
    vm.BatchRequest.PreloadDeltaMm = 0.1; vm.PreviewBatchCommand.Execute(null);
    vm.SelectedVariant = vm.Variants[0]; vm.ApplyBatchCommand.Execute(null);
    Assert(ui.Errors.Count == 1 && vm.Applied.Variants.All(v => v.Contacts[0].PreloadMm == 0.2));
});
await CheckAsync("scan ignores unrelated verification task references in its working subset", async () =>
{
    var (vm, ui) = Demo(); using var owned = vm;
    vm.CopyVariantCommand.Execute(null);
    vm.Draft.Tasks.Add(new VerificationTask { PointId = vm.SelectedContact!.Id });
    vm.SelectedVariant = vm.Variants[0]; vm.ApplyCommand.Execute(null);
    vm.SweepCommand.Execute(null); await Finish(vm);
    Assert(vm.SweepCurrent && ui.Errors.Count == 0 && vm.Applied.Tasks.Count == 1);
});
Directory.CreateDirectory(Path.Combine(root, "docs"));
File.WriteAllText(Path.Combine(root, "docs", "desktop-workflow-verification.json"), JsonSerializer.Serialize(new
{
    utc = DateTimeOffset.UtcNow,
    environment = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    scope = "production ViewModel and Core; no WPF window, dialog, binding or rendering runtime verification",
    count = checks.Count,
    failures,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
Directory.Delete(directory, true);
Console.WriteLine($"RESULT {checks.Count - failures}/{checks.Count} passed");
return failures == 0 ? 0 : 1;

sealed class FakeInteraction : IDesktopInteraction
{
    public string? Save { get; set; }
    public string? Open { get; set; }
    public UnsavedChoice Choice { get; set; } = UnsavedChoice.Discard;
    public List<string> Errors { get; } = [];
    public string? OpenPath(string filter) => Open;
    public string? SavePath(string filter, string suggested) => Save;
    public bool Confirm(string message) => true;
    public UnsavedChoice Unsaved(string message) => Choice;
    public void Error(string message) => Errors.Add(message);
    public void Information(string message) { }
}
