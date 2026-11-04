using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;
using Cabin.Core.IO;
using Cabin.Desktop.ViewModels;
using Cabin.Tests;

var evidence = new List<object>();
var failures = 0;
var outputPath = args.Length > 0 ? args[0] : "test-evidence.json";
var sampleDirectory = args.Length > 1 ? args[1] : "examples";
Directory.CreateDirectory(sampleDirectory);
var temporary = Path.Combine(Path.GetTempPath(), "cabin-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);

void Test(string name, Action body)
{
    try
    {
        body();
        evidence.Add(new { name, status = "passed" });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failures++;
        evidence.Add(new { name, status = "failed", error = error.Message });
        Console.WriteLine("FAIL " + name + " | " + error);
    }
}

async Task AsyncTest(string name, Func<Task> body)
{
    try
    {
        await body();
        evidence.Add(new { name, status = "passed" });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failures++;
        evidence.Add(new { name, status = "failed", error = error.Message });
        Console.WriteLine("FAIL " + name + " | " + error);
    }
}

static void Assert(bool condition, string message = "assertion failed")
{
    if (!condition)
        throw new Exception(message);
}

static void Near(double actual, double expected, double tolerance = 1e-9)
{
    Assert(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance * Math.Max(1, Math.Abs(expected)),
        $"expected {expected:G17}, actual {actual:G17}");
}

static void Reject(Action action, string contains = "")
{
    try
    {
        action();
    }
    catch (ArgumentException error)
    {
        Assert(error.Message.Contains(contains, StringComparison.Ordinal), error.Message);
        return;
    }
    throw new Exception("Expected ArgumentException");
}

static Project Sample() => Demo.Create();
static byte[] Mutate(Action<JsonObject> change)
{
    var root = JsonNode.Parse(ProjectCodec.Encode(Sample()))!.AsObject();
    change(root);
    return Encoding.UTF8.GetBytes(root.ToJsonString());
}

Test("rectangular axial analytic frequency", () =>
{
    var m = new Cavity();
    Near(Modes.Frequency(m, 1, 0, 0), 343.0 / 6.8);
    Near(Modes.Frequency(m, 0, 1, 0), 343.0 / 3.2);
    Near(Modes.Frequency(m, 0, 0, 1), 343.0 / 2.4);
});
Test("tangential analytic frequency", () => Near(Modes.Frequency(new(), 1, 1, 0), 343.0 / 2 * Math.Sqrt(1 / (3.4 * 3.4) + 1 / (1.6 * 1.6))));
Test("enumeration excludes 000 and sorts", () =>
{
    var modes = Modes.Enumerate(new());
    Assert(modes.All(x => x.Nx + x.Ny + x.Nz > 0));
    Assert(modes.SequenceEqual(modes.OrderBy(x => x.Frequency).ThenBy(x => x.Nx).ThenBy(x => x.Ny).ThenBy(x => x.Nz)));
    Assert(modes.All(x => x.Frequency <= 400 + 1e-9));
});
Test("cubic first eigenfrequency triple degeneracy", () =>
{
    var modes = Modes.Enumerate(new() { Length = 2, Width = 2, Height = 2 });
    Near(modes[0].Frequency, 85.75);
    Assert(modes.Take(3).All(x => x.Degeneracy == 3));
});
Test("axial center node and rigid wall extrema", () =>
{
    var m = new Cavity();
    var mode = new Mode(1, 0, 0, 343.0 / 6.8);
    Near(Modes.Shape(mode, m, new(0, 0.5, 0.5)), 1);
    Near(Modes.Shape(mode, m, new(m.Length, 0.5, 0.5)), -1);
    Near(Modes.Shape(mode, m, new(m.Length / 2, 0.5, 0.5)), 0);
});
Test("rigid boundary normal derivative zero", () =>
{
    var m = new Cavity();
    var mode = new Mode(2, 1, 0, 1);
    const double h = 1e-6;
    var slope = (Modes.Shape(mode, m, new(h, 0.3, 0.5)) - Modes.Shape(mode, m, new(0, 0.3, 0.5))) / h;
    Near(slope, 0, 1e-5);
});
Test("mode normalization factors", () =>
{
    Near(new Mode(1, 0, 0, 1).NormFactor, 2);
    Near(new Mode(1, 1, 0, 1).NormFactor, 4);
    Near(new Mode(1, 1, 1, 1).NormFactor, 8);
});
Test("modal resonance analytic damping value", () =>
{
    var m = new Cavity();
    var mode = new Mode(1, 0, 0, 50);
    var value = Modes.Term(mode, m, Complex.One, new(0, 0, 0), 50);
    Near(value.Real, 0);
    Near(value.Imaginary, -2 / m.Volume * 10000 / (2 * m.Damping * 50 * 50));
});
Test("modal resonance inversely scales with damping", () =>
{
    var m = new Cavity();
    var mode = new Mode(1, 0, 0, 50);
    var original = Modes.Term(mode, m, Complex.One, new(), 50).Magnitude;
    m.Damping *= 2;
    Near(Modes.Term(mode, m, Complex.One, new(), 50).Magnitude, original / 2);
});
Test("source on axial node has zero coupling", () =>
{
    var p = Sample();
    p.Sources[0].Point.X = p.Model.Length / 2;
    Near(Modes.SourceCoupling(new(1, 0, 0, 1), p.Model, p.Sources).Magnitude, 0);
});
Test("coincident anti-phase sources cancel", () =>
{
    var p = Sample();
    var second = ProjectCodec.Clone(p.Sources[0]);
    second.Id = Guid.NewGuid();
    second.PhaseDegrees = 180;
    p.Sources.Add(second);
    Assert(Engine.Calculate(p).Reference < 1e-10);
});
Test("doubling all source amplitudes doubles response", () =>
{
    var p = Sample();
    var original = Engine.Calculate(p);
    p.Sources[0].Amplitude *= 2;
    var current = Engine.Calculate(p);
    foreach (var (a, b) in original.Curves[0].Points.Zip(current.Curves[0].Points))
    {
        Near(b.Real, a.Real * 2);
        Near(b.Imaginary, a.Imaginary * 2);
    }
});
Test("source receiver reciprocity", () =>
{
    var p = Sample();
    p.Seats = new() { p.Seats[0] };
    var a = Engine.Calculate(p).Curves[0].Points;
    (p.Sources[0].Point, p.Seats[0].Point) = (p.Seats[0].Point, p.Sources[0].Point);
    var b = Engine.Calculate(p).Curves[0].Points;
    foreach (var pair in a.Zip(b))
    {
        Near(pair.First.Real, pair.Second.Real);
        Near(pair.First.Imaginary, pair.Second.Imaginary);
    }
});
Test("symmetric left right ears give identical response", () =>
{
    var r = Engine.Calculate(Sample());
    foreach (var pair in r.Curves[0].Points.Zip(r.Curves[1].Points))
    {
        Near(pair.First.Real, pair.Second.Real);
        Near(pair.First.Imaginary, pair.Second.Imaginary);
    }
});
Test("frequency grid includes nondivisible endpoint exactly", () =>
{
    var frequencies = Engine.FrequencyGrid(new() { MinimumFrequency = 20, MaximumFrequency = 23.2, FrequencyStep = 1 });
    Assert(frequencies.SequenceEqual(new[] { 20, 21, 22, 23, 23.2 }));
});
Test("complex contributions sum to total", () =>
{
    var p = Sample();
    var r = Engine.Calculate(p);
    var contributions = Engine.Contributions(p, r, p.Seats[0], 61.5);
    var sum = contributions.Aggregate(Complex.Zero, (value, c) => value + Complex.FromPolarCoordinates(c.Magnitude, c.Phase * Math.PI / 180));
    var direct = Engine.At(p, r, p.Seats[0].Point, 61.5);
    Near(sum.Real, direct.Real);
    Near(sum.Imaginary, direct.Imaginary);
});
Test("field maps each plane correctly", () =>
{
    var m = new Cavity();
    var xy = Engine.PlanePoint(m, "xy", 0.25, 0.75, 0.5);
    Near(xy.X, 0.85); Near(xy.Y, 1.2); Near(xy.Z, 0.6);
    var xz = Engine.PlanePoint(m, "xz", 0.25, 0.75, 0.5);
    Near(xz.X, 0.85); Near(xz.Y, 0.8); Near(xz.Z, 0.9);
    var yz = Engine.PlanePoint(m, "yz", 0.25, 0.75, 0.5);
    Near(yz.X, 1.7); Near(yz.Y, 0.4); Near(yz.Z, 0.9);
});
Test("field values agree with direct point evaluation", () =>
{
    var p = Sample(); var r = Engine.Calculate(p);
    var field = Engine.Field(p, r, "xy", 0.7, false, null, 80, 8, 4);
    Assert(field.Cells.Count == 32);
    var c = field.Cells[8];
    Near(c.Value, Engine.At(p, r, Engine.PlanePoint(p.Model, "xy", c.U, c.V, 0.7), 80).Magnitude);
});
Test("invalid field plane and fraction rejected", () =>
{
    var p = Sample(); var r = Engine.Calculate(p);
    Reject(() => Engine.Field(p, r, "invalid", 0.5, false, null, 80));
    Reject(() => Engine.Field(p, r, "xy", double.NaN, false, null, 80));
});
Test("dB relative ratios and finite zero display", () =>
{
    Near(Engine.Decibels(2, 1), 20 * Math.Log10(2));
    Near(Engine.Decibels(0, 1), -120);
    Near(Engine.Decibels(0, 0), -120);
});
Test("empty project saves but cannot calculate", () =>
{
    var p = new Project();
    Assert(Validation.Check(p, false).Count == 0);
    Assert(Validation.Check(p).Any(x => x.Path == "声源"));
    Reject(() => Engine.Calculate(p), "耳点");
    Assert(ProjectCodec.Decode(ProjectCodec.Encode(p)).Seats.Count == 0);
});
Test("nonfinite geometry rejected", () =>
{
    var p = Sample(); p.Model.Length = double.NaN;
    Reject(() => Engine.Calculate(p), "长度");
});
Test("zero damping rejected", () =>
{
    var p = Sample(); p.Model.Damping = 0;
    Reject(() => Engine.Calculate(p), "阻尼");
});
Test("cutoff below band rejected", () =>
{
    var p = Sample(); p.Model.Cutoff = 100;
    Reject(() => Engine.Calculate(p), "截断");
});
Test("equal reversed frequency band rejected", () =>
{
    var p = Sample(); p.Model.MinimumFrequency = 200;
    Reject(() => Engine.Calculate(p), "频带");
});
Test("out of bounds ear rejected with point name", () =>
{
    var p = Sample(); p.Seats[0].Point.X = 10;
    Reject(() => Engine.Calculate(p), "驾驶员 x");
});
Test("points exactly on walls accepted", () =>
{
    var p = Sample(); p.Seats[0].Point = new(0, p.Model.Width, p.Model.Height);
    Assert(Validation.Check(p).Count == 0);
});
Test("duplicate point identity rejected", () =>
{
    var p = Sample(); p.Seats[0].Id = p.Sources[0].Id;
    Reject(() => Engine.Calculate(p), "标识");
});
Test("all disabled sources cannot calculate", () =>
{
    var p = Sample(); p.Sources[0].Enabled = false;
    Reject(() => Engine.Calculate(p), "声源");
});
Test("null point and null collection rejected", () =>
{
    Reject(() => ProjectCodec.Decode(Mutate(r => r["seats"]![0]!["point"] = null)), "缺少字段");
    Reject(() => ProjectCodec.Decode(Mutate(r => r["sources"] = null)), "缺少字段");
});
Test("unknown schema rejected", () => Reject(() => ProjectCodec.Decode(Mutate(r => r["schema"] = 99)), "版本"));
Test("missing geometry field rejected", () => Reject(() => ProjectCodec.Decode(Mutate(r => r["model"]!.AsObject().Remove("width"))), "width"));
Test("unknown JSON field rejected", () => Reject(() => ProjectCodec.Decode(Mutate(r => r["surprise"] = 1)), "JSON"));
Test("duplicate JSON property rejected", () =>
{
    var text = Encoding.UTF8.GetString(ProjectCodec.Encode(Sample()));
    text = text.Replace("\"schema\": 1", "\"schema\": 1, \"schema\": 1");
    Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes(text)), "重复");
});
Test("broken JSON and oversized file rejected", () =>
{
    Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes("{")));
    Reject(() => ProjectCodec.Decode(new byte[Software.MaximumBytes + 1]), "2MiB");
});
Test("native save open retains full project", () =>
{
    var p = Sample();
    var path = Path.Combine(temporary, "roundtrip.cabin.json");
    NativeFiles.Save(path, p);
    var reopened = NativeFiles.Open(path);
    Assert(reopened.Id == p.Id && reopened.Seats[0].Id == p.Seats[0].Id);
    Assert(reopened.Baselines.Count == 1 && reopened.Reviews.Count == 1);
    Near(Engine.Calculate(reopened).Reference, Engine.Calculate(p).Reference);
    Assert(!Directory.EnumerateFiles(temporary, "*.tmp").Any());
});
Test("atomic overwrite saves latest revision", () =>
{
    var path = Path.Combine(temporary, "overwrite.cabin.json"); var p = Sample();
    NativeFiles.Save(path, p); p.Revision = "B"; NativeFiles.Save(path, p);
    Assert(NativeFiles.Open(path).Revision == "B");
});
Test("invalid save preserves existing file", () =>
{
    var path = Path.Combine(temporary, "preserve.cabin.json"); var p = Sample();
    NativeFiles.Save(path, p); var bytes = File.ReadAllBytes(path);
    p.Model.Width = 0;
    Reject(() => NativeFiles.Save(path, p));
    Assert(File.ReadAllBytes(path).SequenceEqual(bytes));
});
Test("CSV point roundtrip retains positions and names", () =>
{
    var p = Sample(); p.Seats[0].Name = "座位,\"A\"";
    var preview = PointCsv.Preview(PointCsv.Export(p), p.Model);
    Assert(preview.CanApply);
    PointCsv.Apply(p, preview);
    Assert(p.Seats[0].Name == "座位,\"A\"");
    Near(p.Seats[0].Point.X, 1.05);
});
Test("CSV invalid cell has line-specific issue and is atomic", () =>
{
    var p = Sample(); var before = ProjectCodec.Encode(p);
    var preview = PointCsv.Preview(PointCsv.Header + "\nsource,源,NaN,0,0,1,0,", p.Model);
    Assert(!preview.CanApply && preview.Issues.Any(x => x.Path == "第2行"));
    Reject(() => PointCsv.Apply(p, preview));
    Assert(before.SequenceEqual(ProjectCodec.Encode(p)));
});
Test("CSV wrong header and malformed quotes rejected", () =>
{
    Assert(!PointCsv.Preview("wrong\n", new()).CanApply);
    Assert(!PointCsv.Preview(PointCsv.Header + "\nseat,\"broken,1,1,1,,,", new()).CanApply);
});
Test("CSV point limits enforced", () =>
{
    var text = PointCsv.Header + "\n" + string.Join("\n", Enumerable.Range(0, 13).Select(i => $"seat,S{i},0,0,0,,,"));
    Assert(!PointCsv.Preview(text, new()).CanApply);
});
Test("multiline point labels rejected before export", () =>
{
    var p = Sample(); p.Seats[0].Name = "line\nnext";
    Reject(() => ProjectCodec.Encode(p), "换行");
});
Test("same baseline comparison is zero ratio", () =>
{
    var p = Sample(); var r = Engine.Calculate(p);
    var diff = Comparison.Compare(p, r, p.Baselines[0]);
    Assert(diff.Seats.Count == 4);
    foreach (var s in diff.Seats) { Near(s.PeakRatioDb, 0); Near(s.RmsRatioDb, 0); }
});
Test("comparison disallows incompatible frequency grid", () =>
{
    var p = Sample(); p.Model.FrequencyStep = 0.5;
    Reject(() => Comparison.Compare(p, Engine.Calculate(p), p.Baselines[0]), "步长");
});
Test("comparison disallows mixed demonstration provenance", () =>
{
    var p = Sample(); p.IsDemo = false;
    Reject(() => Comparison.Compare(p, Engine.Calculate(p), p.Baselines[0]), "来源");
});
Test("comparison warns changed ear location", () =>
{
    var p = Sample(); p.Seats[0].Point.X += 0.1;
    Assert(Comparison.Compare(p, Engine.Calculate(p), p.Baselines[0]).Warnings.Any(x => x.Contains("位置变化")));
});
Test("comparison rejects no matching ear identity", () =>
{
    var p = Sample(); foreach (var seat in p.Seats) seat.Id = Guid.NewGuid();
    Reject(() => Comparison.Compare(p, Engine.Calculate(p), p.Baselines[0]), "匹配");
});
Test("scan has inclusive endpoints and leaves source unchanged", () =>
{
    var p = Sample(); var original = ProjectCodec.Encode(p);
    var result = ParameterScan.Run(p, new("length", 3, 4, 5, p.Seats[0].Id));
    Assert(result.Rows.Count == 5 && result.Rows.All(x => x.Error.Length == 0));
    Near(result.Rows[0].Parameter, 3); Near(result.Rows[^1].Parameter, 4);
    Assert(original.SequenceEqual(ProjectCodec.Encode(p)));
});
Test("scan records invalid geometries instead of ranking them", () =>
{
    var p = Sample(); var scan = ParameterScan.Run(p, new("length", 0.5, 4, 3, p.Seats[0].Id));
    Assert(scan.Rows[0].Error.Contains("x / m"));
    Assert(scan.Rows[^1].Error.Length == 0);
});
Test("scan invalid settings rejected", () =>
{
    var p = Sample();
    Reject(() => ParameterScan.Run(p, new("length", 3, 4, 1, p.Seats[0].Id)));
    Reject(() => ParameterScan.Run(p, new("length", 3, 4, 5, Guid.Empty)));
    Reject(() => ParameterScan.Run(p, new("invalid", 3, 4, 5, p.Seats[0].Id)));
});
Test("cancellation token interrupts computation", () =>
{
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { Engine.Calculate(Sample(), cancellation.Token); }
    catch (OperationCanceledException) { return; }
    throw new Exception("No cancellation");
});
Test("density bands conserve mode count", () =>
{
    var r = Engine.Calculate(Sample());
    var density = Diagnostics.Density(r);
    Assert(density.Sum(x => x.Count) == r.Modes.Count);
    Assert(density.All(x => x.Count == x.Axial + x.Tangential + x.Oblique));
});
Test("truncation extension preserves input and adds modes", () =>
{
    var p = Sample(); var before = ProjectCodec.Encode(p); var r = Engine.Calculate(p);
    var study = Diagnostics.Extend(p, r);
    Assert(study.ExtendedModes > study.OriginalModes && study.Rows.Count == 4);
    Assert(before.SequenceEqual(ProjectCodec.Encode(p)));
});
Test("response exports retain identity provenance units complex parts", () =>
{
    var p = Sample(); var text = ResearchExports.Responses(p, Engine.Calculate(p));
    Assert(text.Contains(p.Id.ToString()) && text.Contains("demo=True"));
    Assert(text.Contains("real_relative,imag_relative") && text.Contains("不等于实车绝对声压"));
});
Test("scan export leaves invalid numeric cells empty", () =>
{
    var p = Sample(); var scan = ParameterScan.Run(p, new("length", 0.5, 4, 3, p.Seats[0].Id));
    var text = ResearchExports.Scan(p, scan);
    Assert(text.Contains("0.5,,,,,"));
});
Test("research report includes actual conditions and limitation", () =>
{
    var p = Sample(); var text = ResearchExports.Report(p, Engine.Calculate(p), null, null, null);
    Assert(text.Contains("非Pa，非dB SPL") && text.Contains("模拟数据，非实测"));
    Assert(text.Contains("343") && text.Contains(p.Sources[0].Name));
});
Test("blank MVVM state does not auto load data", () =>
{
    var vm = new WorkspaceViewModel(new TestServices());
    Assert(!vm.HasProject && vm.Result is null && !vm.SaveCommand.CanExecute(null));
    vm.NewCommand.Execute(null);
    Assert(vm.Editor!.Sources.Count == 0 && vm.Editor.Seats.Count == 0);
});
Test("numeric draft rejects malformed text without silent fallback", () =>
{
    var vm = new WorkspaceViewModel(new TestServices()); vm.ExampleCommand.Execute(null);
    vm.Editor!.Length.Text = "bad";
    Assert(vm.Editor.Length.HasErrors);
    Reject(() => vm.Editor.Build(true), "长度");
});
Test("new project cancellation preserves current draft", () =>
{
    var services = new TestServices { SaveDecision = 0 };
    var vm = new WorkspaceViewModel(services); vm.ExampleCommand.Execute(null);
    var editor = vm.Editor;
    vm.NewCommand.Execute(null);
    Assert(ReferenceEquals(vm.Editor, editor));
});
Test("save invalid draft does not close project", () =>
{
    var services = new TestServices { SaveDecision = 1, Path = Path.Combine(temporary, "invalid.cabin.json") };
    var vm = new WorkspaceViewModel(services); vm.ExampleCommand.Execute(null);
    vm.Editor!.Width.Text = "bad";
    Assert(!vm.CanClose() && services.Errors.Count == 1);
});
Test("MVVM save reopen and stable source provenance", () =>
{
    var services = new TestServices { Path = Path.Combine(temporary, "vm.cabin.json") };
    var vm = new WorkspaceViewModel(services); vm.ExampleCommand.Execute(null);
    vm.SaveCommand.Execute(null);
    Assert(!vm.Dirty && File.Exists(services.Path));
    vm.NewCommand.Execute(null); vm.OpenCommand.Execute(null);
    Assert(vm.Editor!.IsDemo && vm.Editor.Sources.Count == 1 && vm.Result is null);
});
await AsyncTest("MVVM calculate input change invalidates stale results", async () =>
{
    var services = new TestServices(); var vm = new WorkspaceViewModel(services);
    vm.ExampleCommand.Execute(null); vm.CalculateCommand.Execute(null);
    await Wait(vm);
    Assert(vm.Result is not null && vm.Field is not null, string.Join("|", services.Errors));
    vm.Editor!.Sources[0].Amplitude.Text = "2";
    Assert(vm.Result is null && vm.Field is null && !vm.ExportReportCommand.CanExecute(null));
});
await AsyncTest("MVVM baseline capture and scan apply use current computed inputs", async () =>
{
    var services = new TestServices(); var vm = new WorkspaceViewModel(services);
    vm.ExampleCommand.Execute(null); vm.CalculateCommand.Execute(null); await Wait(vm);
    vm.CaptureBaselineCommand.Execute(null);
    Assert(vm.Baselines.Count == 2);
    vm.ScanCount.Text = "3"; vm.ScanCommand.Execute(null); await Wait(vm);
    Assert(vm.Scan is not null && vm.Scan.Rows.Count == 3, string.Join("|", services.Errors));
    var expected = vm.SelectedScanRow!.Parameter;
    vm.ApplyScanCommand.Execute(null);
    Near(vm.Editor!.Length.Value, expected);
    Assert(vm.Result is null && vm.Dirty);
});
await AsyncTest("MVVM fractional scan count rejected", async () =>
{
    var services = new TestServices(); var vm = new WorkspaceViewModel(services);
    vm.ExampleCommand.Execute(null); vm.CalculateCommand.Execute(null); await Wait(vm);
    vm.ScanCount.Text = "3.5"; vm.ScanCommand.Execute(null); await Wait(vm);
    Assert(vm.Scan is null && services.Errors.Any(x => x.Contains("整数")));
});

Test("field rejects invalid resolution and frequency", () =>
{
    var p = Sample(); var r = Engine.Calculate(p);
    Reject(() => Engine.Field(p, r, "xy", 0.5, false, null, 50, 0, 10), "网格");
    Reject(() => Engine.Field(p, r, "xy", 0.5, false, null, double.NaN), "频率");
    Reject(() => Engine.Field(p, r, "xy", 0.5, true, null, 50), "模态");
});
Test("density rejects unbounded number of bands", () =>
{
    Reject(() => Diagnostics.Density(Engine.Calculate(Sample()), 1e-10), "10000");
});
Test("CSV apply revalidates mutable preview before mutation", () =>
{
    var p = Sample(); var before = ProjectCodec.Encode(p);
    var preview = PointCsv.Preview(PointCsv.Export(p), p.Model);
    preview.Rows[0].Point.X = 100;
    Reject(() => PointCsv.Apply(p, preview), "x / m");
    Assert(before.SequenceEqual(ProjectCodec.Encode(p)));
});
Test("scan cancellation interrupts candidate calculation", () =>
{
    using var token = new CancellationTokenSource(); token.Cancel();
    var p = Sample();
    try { ParameterScan.Run(p, new("length", 3, 4, 5, p.Seats[0].Id), token.Token); }
    catch (OperationCanceledException) { return; }
    throw new Exception("No scan cancellation");
});

var demo = Sample();
NativeFiles.Save(Path.Combine(sampleDirectory, "four-seat-demo.cabin.json"), demo);
NativeFiles.Save(Path.Combine(sampleDirectory, "blank.cabin.json"), new Project());
NativeFiles.WriteText(Path.Combine(sampleDirectory, "points.csv"), PointCsv.Export(demo));
NativeFiles.WriteText(Path.Combine(sampleDirectory, "response.csv"), ResearchExports.Responses(demo, Engine.Calculate(demo)));
NativeFiles.WriteText(Path.Combine(sampleDirectory, "research-report.txt"), ResearchExports.Report(demo, Engine.Calculate(demo), null, null, null));
var report = new
{
    executedUtc = DateTimeOffset.UtcNow,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    note = "Core and shared MVVM logic executed on Linux. CommandManager shim is test-only. WPF bindings, dialogs, rendering and Windows executable were not executed by this suite.",
    total = evidence.Count,
    failed = failures,
    results = evidence
};
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Directory.Delete(temporary, true);
Console.WriteLine($"{evidence.Count - failures}/{evidence.Count} passed");
return failures > 0 ? 1 : 0;

static async Task Wait(WorkspaceViewModel vm)
{
    var started = DateTime.UtcNow;
    while (vm.Busy)
    {
        if (DateTime.UtcNow - started > TimeSpan.FromSeconds(40))
            throw new TimeoutException("VM calculation timed out");
        await Task.Delay(10);
    }
}
