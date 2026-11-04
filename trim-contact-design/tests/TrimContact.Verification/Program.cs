using System.Globalization;
using System.Text.Json;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

var checks = new List<object>();
var failures = new List<string>();
void Check(string name, Action run)
{
    try
    {
        run();
        checks.Add(new { name, passed = true });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        checks.Add(new { name, passed = false, error = error.Message });
        failures.Add(name);
        Console.WriteLine("FAIL " + name + " : " + error.Message);
    }
}
void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new Exception(message);
}
void Near(double actual, double expected, double tolerance = 1e-7) => Assert(Math.Abs(actual - expected) <= tolerance,
    $"expected {expected:G17}, actual {actual:G17}");
void Reject(Action action)
{
    bool rejected = false;
    try { action(); }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException) { rejected = true; }
    Assert(rejected, "Invalid input was accepted");
}
DesignProject Symmetric(bool contact = false)
{
    var p = Demonstration.Create();
    p.Settings.EnumerateTolerance = false;
    var v = p.Variants[0];
    if (!contact) v.Contacts.Clear();
    else
    {
        v.Contacts[0].PreloadMm = 0.2;
        v.Contacts[0].GapMm = 0;
        v.Contacts[0].ToleranceMm = 0;
    }
    return p;
}
PhaseState Solve(DesignProject p, double normal, double shear, params double[] tolerance) =>
    new ContactSolver(p.Panel, p.Variants[0]).Solve(0, normal, shear,
        tolerance.Length == 0 ? new double[p.Variants[0].Contacts.Count] : tolerance);

Check("empty project may save but cannot calculate", () =>
{
    var p = new DesignProject();
    ProjectValidation.Require(p);
    Reject(() => AnalysisEngine.Run(p));
});
Check("fixed demonstration validates", () => ProjectValidation.Require(Demonstration.Create(), true));
Check("independent symmetric static 1g displacement", () => Near(Solve(Symmetric(), 1, 0).Displacement.Z, 0.024516625));
Check("independent symmetric clip force", () =>
{
    foreach (var point in Solve(Symmetric(), 1, 0).Clips) Near(point.NormalN, 2.4516625);
});
Check("zero load zero deflection without contacts", () => Near(Solve(Symmetric(), 0, 0).Displacement.Norm, 0));
Check("normal load sign reversal", () => Near(Solve(Symmetric(), -1, 0).Displacement.Z, -0.024516625));
Check("mass scaling", () =>
{
    var p = Symmetric(); p.Panel.MassKg = 2;
    Near(Solve(p, 1, 0).Displacement.Z, 0.04903325);
});
Check("preload equilibrium center displacement", () => Near(Solve(Symmetric(true), 0, 0).Displacement.Z, -0.04));
Check("preload equilibrium compression", () => Near(Solve(Symmetric(true), 0, 0).Contacts[0].CompressionMm, 0.16));
Check("preload equilibrium reaction force", () => Near(Solve(Symmetric(true), 0, 0).Contacts[0].NormalN, 16));
Check("preload clip reaction balance", () =>
{
    foreach (var point in Solve(Symmetric(true), 0, 0).Clips) Near(point.NormalN, -4);
});
Check("open contact zero force", () =>
{
    var p = Symmetric(true); p.Variants[0].Contacts[0].GapMm = 1;
    Near(Solve(p, 0, 0).Contacts[0].NormalN, 0);
});
Check("positive gap tolerance reduces compression", () => Near(Solve(Symmetric(true), 0, 0, 0.1).Contacts[0].CompressionMm, 0.08));
Check("negative gap tolerance increases compression", () => Near(Solve(Symmetric(true), 0, 0, -0.1).Contacts[0].CompressionMm, 0.24));
Check("contact cannot pull", () =>
{
    var state = Solve(Symmetric(true), -20, 0);
    Near(state.Contacts[0].NormalN, 0);
    Assert(state.Clips.All(c => c.NormalN < 0));
});
Check("independent tangential displacement for 10N", () => Near(Solve(Symmetric(), 0, 10 / Product.Gravity).TangentialMm, 0.05));
Check("negative tangential symmetry", () => Near(Solve(Symmetric(), 0, -10 / Product.Gravity).TangentialMm, -0.05));
Check("sticking contact shares shear force", () =>
{
    var p = Symmetric(true); var c = p.Variants[0].Contacts[0]; c.Friction = 1; c.TangentialStiffness = 40;
    var s = Solve(p, 0, 1 / Product.Gravity);
    Near(s.TangentialMm, 1.0 / 240);
    Near(s.Contacts[0].SlipMm, 0);
});
Check("Coulomb cap and shear closure", () =>
{
    var p = Symmetric(true); var c = p.Variants[0].Contacts[0]; c.Friction = 0.1;
    var s = Solve(p, 0, 20 / Product.Gravity);
    Near(s.Contacts[0].ShearN, 1.6);
    Near(s.TangentialMm, (20 - 1.6) / 200);
    Assert(s.Contacts[0].SlipMm > 0);
});
Check("zero friction accepted and no contact shear", () =>
{
    var p = Symmetric(true); p.Variants[0].Contacts[0].Friction = 0;
    var s = Solve(p, 0, 1);
    Near(s.Contacts[0].ShearN, 0);
    Assert(s.Contacts[0].SlipMm > 0);
});
Check("detached contact has no reported slip", () =>
{
    var p = Symmetric(true); p.Variants[0].Contacts[0].GapMm = 2;
    var s = Solve(p, 0, 1);
    Near(s.Contacts[0].SlipMm, 0);
});
Check("asymmetric offset contact includes rotations", () =>
{
    var p = Symmetric(true); p.Variants[0].Contacts[0].Xmm = 300;
    Assert(Math.Abs(Solve(p, 0, 0).Displacement.X) > 1e-4);
});
Check("rotation and force independently balanced", () =>
{
    var p = Symmetric(true); p.Variants[0].Contacts[0].Xmm = 300; p.Variants[0].Contacts[0].Ymm = 80;
    var report = AnalysisEngine.Run(p); var rows = BalanceAudit.Evaluate(p, report);
    Assert(rows.All(r => Math.Abs(r.NormalErrorN) < 1e-6 && Math.Abs(r.MomentXErrorNmm) < 1e-4
        && Math.Abs(r.MomentYErrorNmm) < 1e-4 && Math.Abs(r.ShearErrorN) < 1e-6));
});
Check("24 phases include 90 degrees", () =>
{
    var p = Symmetric(); var r = AnalysisEngine.Run(p); var s = r.Variants[0].NominalTraces[0].Samples;
    Assert(s.Count == 24 && s.Any(x => x.PhaseDegrees == 90) && !s.Any(x => x.PhaseDegrees == 360));
});
Check("zero tolerances not duplicated", () => Assert(AnalysisEngine.ToleranceCases(Symmetric(true).Variants[0], true).Count() == 1));
Check("single nonzero tolerance gives nominal and 2 corners", () => Assert(AnalysisEngine.Run(Demonstration.Create()).Variants[0].ToleranceCases == 3));
Check("six tolerances give nominal and 64 corners", () =>
{
    var p = Demonstration.Create(); var v = p.Variants[0];
    for (int i = 1; i < 6; i++) v.Contacts.Add(new ContactPoint { Name = "Pad" + i, Xmm = 40 + i * 50, Ymm = 140 });
    Assert(AnalysisEngine.ToleranceCases(v, true).Count() == 65);
    var r = AnalysisEngine.Run(p); Assert(r.Variants[0].EvaluatedStates == 65 * 24);
});
Check("all states residual bounded", () => Assert(AnalysisEngine.Run(Demonstration.Create()).Variants[0].MaxResidualN < 1e-6));
Check("cancellation honored", () =>
{
    using var c = new CancellationTokenSource(); c.Cancel();
    bool canceled = false;
    try { AnalysisEngine.Run(Demonstration.Create(), cancellation: c.Token); } catch (OperationCanceledException) { canceled = true; }
    Assert(canceled);
});
Check("parameter sweep independently changes outcomes and preserves input", () =>
{
    var p = Demonstration.Create(); var v = p.Variants[0]; var before = ProjectCodec.Serialize(p);
    var rows = AnalysisEngine.Sweep(p, v.Id, v.Contacts[0].Id, "预紧量mm", 0, 0.6, 4);
    Assert(rows.Count == 4 && rows[0].MaxCompressionMm < rows[3].MaxCompressionMm);
    Assert(ProjectCodec.Serialize(p) == before);
});
Check("sweep invalid range rejected", () =>
{
    var p = Demonstration.Create(); var v = p.Variants[0];
    Reject(() => AnalysisEngine.Sweep(p, v.Id, v.Contacts[0].Id, "预紧量mm", -1, 1, 4));
});
Check("source change invalidates results", () =>
{
    var p = Demonstration.Create(); var r = AnalysisEngine.Run(p); p.Source += " changed";
    Reject(() => ResultExport.Csv(p, r));
});
Check("task edits do not change input hash", () =>
{
    var p = Demonstration.Create(); var hash = ProjectCodec.InputHash(p); p.Tasks.Add(new VerificationTask());
    Assert(ProjectCodec.InputHash(p) == hash);
});
Check("unknown schema rejected", () =>
{
    var p = Demonstration.Create(); p.Schema = 2; Reject(() => ProjectValidation.Require(p));
});
Check("unknown field rejected", () => Reject(() => ProjectCodec.Parse(ProjectCodec.Serialize(Demonstration.Create()).Replace("\"schema\": 1", "\"schema\": 1, \"unknown\": 3"))));
Check("missing field rejected", () => Reject(() => ProjectCodec.Parse(ProjectCodec.Serialize(Demonstration.Create()).Replace("\"massKg\": 1,", ""))));
Check("duplicate JSON field rejected", () => Reject(() => ProjectCodec.Parse(ProjectCodec.Serialize(Demonstration.Create()).Replace("\"schema\": 1", "\"schema\": 1, \"schema\": 1"))));
Check("null arrays rejected", () => Reject(() => ProjectCodec.Parse(ProjectCodec.Serialize(new DesignProject()).Replace("\"variants\": []", "\"variants\": null"))));
Check("truncated JSON rejected", () => Reject(() => ProjectCodec.Parse("{\"schema\":")));
Check("oversize project rejected", () => Reject(() => ProjectCodec.Parse(new string(' ', ProjectCodec.MaxBytes + 1))));
Check("duplicate point IDs rejected", () =>
{
    var p = Demonstration.Create(); p.Variants[0].Clips[1].Id = p.Variants[0].Clips[0].Id;
    Reject(() => ProjectValidation.Require(p));
});
Check("nonfinite mass rejected", () =>
{
    var p = Demonstration.Create(); p.Panel.MassKg = double.NaN; Reject(() => ProjectValidation.Require(p));
});
Check("zero mass rejected", () =>
{
    var p = Demonstration.Create(); p.Panel.MassKg = 0; Reject(() => ProjectValidation.Require(p));
});
Check("zero stiffness rejected", () =>
{
    var p = Demonstration.Create(); p.Variants[0].Clips[0].NormalStiffness = 0; Reject(() => ProjectValidation.Require(p));
});
Check("out of panel point rejected", () =>
{
    var p = Demonstration.Create(); p.Variants[0].Contacts[0].Xmm = 401; Reject(() => ProjectValidation.Require(p));
});
Check("self intersection rejected", () =>
{
    var p = Demonstration.Create(); (p.Panel.Outline[1], p.Panel.Outline[2]) = (p.Panel.Outline[2], p.Panel.Outline[1]);
    Reject(() => ProjectValidation.Require(p));
});
Check("collinear clips rejected", () =>
{
    var p = Demonstration.Create(); foreach (var clip in p.Variants[0].Clips) clip.Ymm = 150;
    Reject(() => ProjectValidation.Require(p, true));
});
Check("too many contacts rejected", () =>
{
    var p = Demonstration.Create(); for (int i = 0; i < 6; i++) p.Variants[0].Contacts.Add(new ContactPoint());
    Reject(() => ProjectValidation.Require(p));
});
Check("polygon boundary accepted", () => Assert(Geometry.Contains(Demonstration.Create().Panel.Outline, 400, 150)));
Check("completed task needs evidence", () =>
{
    var p = Demonstration.Create(); p.Tasks.Add(new VerificationTask { Completed = true }); Reject(() => ProjectValidation.Require(p));
});
Check("invalid task reference rejected", () =>
{
    var p = Demonstration.Create(); p.Tasks.Add(new VerificationTask { PointId = Guid.NewGuid() }); Reject(() => ProjectValidation.Require(p));
});
Check("task generation deduplicates", () =>
{
    var p = Demonstration.Create(); var report = AnalysisEngine.Run(p);
    Assert(DesignOperations.GenerateTasks(p, report) > 1); Assert(DesignOperations.GenerateTasks(p, report) == 0);
});
Check("delete contact retains task detached", () =>
{
    var p = Demonstration.Create(); var id = p.Variants[0].Contacts[0].Id;
    p.Tasks.Add(new VerificationTask { PointId = id }); DesignOperations.DetachTasks(p, [id]); p.Variants[0].Contacts.Clear();
    Assert(p.Tasks[0].PointId is null); ProjectValidation.Require(p);
});
Check("copy variant creates fresh identities", () =>
{
    var v = Demonstration.Create().Variants[0]; var copy = DesignOperations.CopyVariant(v, "copy");
    Assert(copy.Id != v.Id && copy.Clips[0].Id != v.Clips[0].Id && copy.Contacts[0].Id != v.Contacts[0].Id);
});
Check("session edit rollback on validation error", () =>
{
    var s = new ProjectSession(); s.Load(Demonstration.Create());
    Reject(() => s.Edit("bad", p => p.Panel.MassKg = 0)); Near(s.Current.Panel.MassKg, 1);
});
Check("undo redo applied edit", () =>
{
    var s = new ProjectSession(); s.Load(Demonstration.Create()); s.Edit("mass", p => p.Panel.MassKg = 2);
    s.Undo(); Near(s.Current.Panel.MassKg, 1); s.Redo(); Near(s.Current.Panel.MassKg, 2);
});
Check("archive blocks subsequent edit but copy allows", () =>
{
    var s = new ProjectSession(); s.Load(Demonstration.Create()); s.Edit("archive", p => p.Archived = true);
    Reject(() => s.Edit("bad", p => p.Name = "changed")); var id = s.Current.Id; s.CopyProject();
    Assert(s.Current.Id != id && !s.Current.Archived); s.Edit("ok", p => p.Name = "new");
});
var directory = Path.Combine(Path.GetTempPath(), "trim-verify-" + Guid.NewGuid().ToString("N"));
var store = new ProjectStore(directory);
Check("disk save reopen content and IDs", () =>
{
    var p = Demonstration.Create(); var result = store.Save(p, null); var reopened = store.Open(p.Id);
    Assert(ProjectCodec.Serialize(p) == ProjectCodec.Serialize(reopened.Project)); Assert(result.Hash == reopened.Hash);
});
Check("overwrite backs up prior file", () =>
{
    var p = Demonstration.Create(); var saved = store.Save(p, null); p.Name = "second"; store.Save(p, saved.Hash);
    Assert(store.Open(p.Id, true).Project.Name != "second");
});
Check("external file change blocks overwrite", () =>
{
    var p = Demonstration.Create(); var saved = store.Save(p, null); File.AppendAllText(saved.Path, "\n");
    Reject(() => store.Save(p, saved.Hash));
});
Check("same ID import does not overwrite", () =>
{
    var p = Demonstration.Create(); store.Save(p, null); Reject(() => store.Save(p, null));
});
Check("portable embedded project opens in different library", () =>
{
    var p = Demonstration.Create(); var a = store.Save(p, null); var b = new ProjectStore(directory + "-moved");
    var imported = ProjectCodec.Parse(File.ReadAllText(a.Path)); b.Save(imported, null);
    Assert(ProjectCodec.InputHash(b.Open(imported.Id).Project) == ProjectCodec.InputHash(p));
});
Check("corrupt library item visible as error", () =>
{
    File.WriteAllText(store.FilePath(Guid.NewGuid()), "bad"); Assert(store.List().Any(p => p.Error is not null));
});
Check("project Unicode round trip", () =>
{
    var p = Demonstration.Create(); p.Name = "门板,\"压缩\"\n核查";
    Assert(ProjectCodec.Parse(ProjectCodec.Serialize(p)).Name == p.Name);
});
Check("CSV template preflight replace", () =>
{
    var p = Demonstration.Create(); var preview = LayoutCsv.Preview(p, p.Variants[0].Id, LayoutCsv.Template, true);
    Assert(preview.ClipCount == 4 && preview.ContactCount == 1 && preview.Proposed.Variants[0].Clips.Count == 4);
});
Check("CSV quoted multiline and escaped quote", () =>
{
    var rows = CsvTable.Read("a,b\r\n\"x,\"\"y\"\"\nz\",3\r\n");
    Assert(rows[1].Cells[0] == "x,\"y\"\nz");
});
Check("CSV malformed quoting rejected", () => Reject(() => CsvTable.Read("a,b\n\"x,y")));
Check("CSV header mismatch rejected", () =>
{
    var p = Demonstration.Create(); Reject(() => LayoutCsv.Preview(p, p.Variants[0].Id, LayoutCsv.Template.Replace("x_mm", "X_MM"), true));
});
Check("CSV invalid row rolls back", () =>
{
    var p = Demonstration.Create(); var before = ProjectCodec.Serialize(p);
    Reject(() => LayoutCsv.Preview(p, p.Variants[0].Id, LayoutCsv.Template.Replace("100,50", "bad,50"), true));
    Assert(ProjectCodec.Serialize(p) == before);
});
Check("CSV expired preview blocked", () =>
{
    var p = Demonstration.Create(); var preview = LayoutCsv.Preview(p, p.Variants[0].Id, LayoutCsv.Template, true); p.Name = "changed";
    Reject(() => LayoutCsv.Apply(p, preview));
});
Check("batch preview and apply preserve original", () =>
{
    var p = Demonstration.Create(); var request = new BatchLayoutRequest { PreloadDeltaMm = 0.1 };
    var preview = BatchLayout.Preview(p, p.Variants[0].Id, request); var next = BatchLayout.Apply(p, request, preview);
    Near(p.Variants[0].Contacts[0].PreloadMm, 0.2); Near(next.Variants[0].Contacts[0].PreloadMm, 0.3);
});
Check("batch out of outline rejects all changes", () =>
{
    var p = Demonstration.Create(); Reject(() => BatchLayout.Preview(p, p.Variants[0].Id, new BatchLayoutRequest { ShiftXmm = 1000 }));
});
Check("batch no change rejected", () =>
{
    var p = Demonstration.Create(); Reject(() => BatchLayout.Preview(p, p.Variants[0].Id, new BatchLayoutRequest()));
});
Check("batch request changed after preview rejected", () =>
{
    var p = Demonstration.Create(); var request = new BatchLayoutRequest { ShiftXmm = 10 }; var preview = BatchLayout.Preview(p, p.Variants[0].Id, request);
    request.ShiftXmm = 20; Reject(() => BatchLayout.Apply(p, request, preview));
});
Check("revision difference identifies common load change", () =>
{
    var p = Demonstration.Create(); var b = ProjectCodec.Clone(p); b.Loads[0].NormalAmplitudeG = 2;
    var report = ProjectDifference.Compare(p, b); Assert(!report.SameSharedConditions && report.Rows.Count == 1);
});
Check("revision difference identifies only design change", () =>
{
    var p = Demonstration.Create(); var b = ProjectCodec.Clone(p); b.Variants[0].Contacts[0].PreloadMm += 0.1;
    var report = ProjectDifference.Compare(p, b); Assert(report.SameSharedConditions && report.Rows.Count == 1);
});
Check("quality review source and tolerance sensitivity", () =>
{
    var p = Demonstration.Create(); var rows = QualityInspector.Inspect(p, AnalysisEngine.Run(p));
    Assert(rows.Any(r => r.Severity == "来源") && rows.Any(r => r.Severity == "敏感"));
});
Check("HTML escapes user content", () =>
{
    var p = Demonstration.Create(); p.Name = "<script>alert(1)</script>"; var html = ResultExport.Html(p, AnalysisEngine.Run(p));
    Assert(!html.Contains("<script>alert") && html.Contains("&lt;script&gt;"));
});
Check("CSV formula injection neutralized", () =>
{
    var p = Demonstration.Create(); p.Variants[0].Name = "=cmd()"; var csv = ResultExport.Csv(p, AnalysisEngine.Run(p));
    Assert(csv.Contains("'=cmd()"));
});
Check("CSV negative physical numbers remain numeric", () =>
{
    var p = Symmetric(true); var r = AnalysisEngine.Run(p); var csv = ResultExport.TraceCsv(p, r);
    Assert(csv.Contains("\"-4\"") && !csv.Contains("\"'-4\""));
});
Check("exports preserve source units method and input hash", () =>
{
    var p = Demonstration.Create(); var r = AnalysisEngine.Run(p);
    Assert(ResultExport.Json(p, r).Contains(r.InputHash));
    Assert(ResultExport.Csv(p, r).Contains("mm") && ResultExport.Csv(p, r).Contains(r.Method));
    Assert(BalanceExport.Csv(p, r).Contains(r.InputHash));
});
Check("frequency is label only", () =>
{
    var p = Demonstration.Create(); var a = AnalysisEngine.Run(p).Variants[0].NominalTraces[0].Samples; p.Loads[0].FrequencyHz = 100;
    var b = AnalysisEngine.Run(p).Variants[0].NominalTraces[0].Samples;
    for (int i = 0; i < a.Count; i++) Near(a[i].Displacement.Z, b[i].Displacement.Z);
});
Check("layout exchange CSV round trip preserves parameter values", () =>
{
    var p = Demonstration.Create(); var v = p.Variants[0]; var csv = LayoutCsv.Export(v);
    var next = LayoutCsv.Preview(p, v.Id, csv, true).Proposed.Variants[0];
    Near(next.Contacts[0].PreloadMm, v.Contacts[0].PreloadMm); Near(next.Clips[0].NormalStiffness, v.Clips[0].NormalStiffness);
});
Check("randomized asymmetric contact states satisfy equilibrium and unilateral laws", () =>
{
    var random = new Random(521);
    for (int iteration = 0; iteration < 200; iteration++)
    {
        var p = Symmetric(true); var c = p.Variants[0].Contacts[0];
        c.Xmm = 20 + 360 * random.NextDouble(); c.Ymm = 20 + 260 * random.NextDouble();
        c.PreloadMm = random.NextDouble(); c.GapMm = random.NextDouble();
        c.NormalStiffness = 10 + 990 * random.NextDouble(); c.Friction = random.NextDouble();
        var s = Solve(p, -3 + 6 * random.NextDouble(), -2 + 4 * random.NextDouble());
        Near(s.Clips.Sum(x => x.NormalN) + s.Contacts.Sum(x => x.NormalN), s.NormalForceN, 1e-5);
        Near(s.Clips.Sum(x => x.ShearN) + s.Contacts.Sum(x => x.ShearN), s.TangentialForceN, 1e-6);
        Assert(s.Contacts.All(x => x.NormalN >= 0 && x.CompressionMm >= 0 && Math.Abs(x.ShearN) <= c.Friction * x.NormalN + 1e-7));
    }
});
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(Path.Combine(root, "docs"));
Directory.CreateDirectory(Path.Combine(root, "samples"));
File.WriteAllText(Path.Combine(root, "docs", "core-verification.json"), JsonSerializer.Serialize(new
{
    utc = DateTimeOffset.UtcNow,
    environment = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " / " + System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    count = checks.Count,
    failures = failures.Count,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(Path.Combine(root, "samples", "门板接触-模拟.trimproj"), ProjectCodec.Serialize(Demonstration.Create()));
File.WriteAllText(Path.Combine(root, "samples", "布置导入模板-模拟.csv"), LayoutCsv.Template);
Console.WriteLine($"RESULT {checks.Count - failures.Count}/{checks.Count} passed");
if (failures.Count > 0) Environment.Exit(1);
