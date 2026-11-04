using System.Text.Json;
using System.Text.Json.Nodes;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

var checks = new List<object>();
var failures = new List<string>();
void Check(string name, Action action)
{
    try
    {
        action();
        checks.Add(new { name, passed = true });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        checks.Add(new { name, passed = false, reason = ex.Message });
        failures.Add(name + ": " + ex.Message);
        Console.WriteLine("FAIL " + name + " " + ex.Message);
    }
}
void Assert(bool condition, string message = "断言不符")
{
    if (!condition) throw new Exception(message);
}
void Near(double actual, double expected, double tolerance = 1e-8)
{
    Assert(Math.Abs(actual - expected) <= tolerance, $"实际 {actual:R}，预期 {expected:R}");
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { return; }
    throw new Exception("应拒绝而未拒绝");
}
var demo = Demonstration.Create();
Check("示例项目全量校验", () => ProjectValidator.ThrowIfInvalid(demo));
Check("人工手算1800转机械30电气60槽360齿槽360", () =>
{
    var c = FrequencyEngine.Candidates(demo.Motor, demo.CustomOrders).ToDictionary(x => x.Id);
    Near(FrequencyEngine.At(c["rotation"].Order, 1800), 30);
    Near(FrequencyEngine.At(c["electrical"].Order, 1800), 60);
    Near(FrequencyEngine.At(c["slot"].Order, 1800), 360);
    Near(FrequencyEngine.At(c["cogging"].Order, 1800), 360);
});
foreach (var rpm in new double[] { 0, 600, 1800, 3600, 100000 })
{
    Check("多转速手算 n=" + rpm, () =>
    {
        Near(FrequencyEngine.At(1, rpm), rpm / 60);
        Near(FrequencyEngine.At(2, rpm), rpm / 30);
        Near(FrequencyEngine.At(12, rpm), rpm / 5);
    });
}
Check("12槽10极理想齿槽60阶", () => Near(FrequencyEngine.LeastCommonMultiple(12, 10), 60));
Check("分数阶次0.5", () => Near(FrequencyEngine.At(0.5, 1800), 15));
Check("非法负阶与负转速", () => { Reject(() => FrequencyEngine.At(-1, 0)); Reject(() => FrequencyEngine.At(1, -1)); });
Check("非有限数值", () => { Reject(() => FrequencyEngine.At(double.NaN, 0)); Reject(() => FrequencyEngine.At(1, double.PositiveInfinity)); });
Check("有刷单刷10片300Hz且不输出同步电频槽齿槽", () =>
{
    var motor = ProjectCodec.Clone(demo.Motor);
    motor.Topology = MotorTopology.BrushedPermanentMagnet;
    var c = FrequencyEngine.Candidates(motor, []);
    Assert(c.Count == 2);
    Near(FrequencyEngine.At(c.Single(x => x.Id == "commutator").Order, 1800), 300);
    Assert(c.All(x => x.Id is not ("electrical" or "slot" or "cogging")));
});
Check("齿槽假设缺失拒绝", () =>
{
    var p = ProjectCodec.Clone(demo);
    p.Motor.SymmetricUnskewedAssumption = false;
    Reject(() => ProjectValidator.ThrowIfInvalid(p));
});
Check("未启用齿槽不自动提供候选", () =>
{
    var p = ProjectCodec.Clone(demo);
    p.Motor.IncludeIdealCogging = false;
    Assert(FrequencyEngine.Candidates(p.Motor, []).All(x => x.Id != "cogging"));
});
foreach (var invalid in new[] { "reversed", "step", "inspect", "zeroPole", "slots", "NaN", "tooManySteps", "negative" })
{
    Check("输入边界 " + invalid, () =>
    {
        var p = ProjectCodec.Clone(demo);
        switch (invalid)
        {
            case "reversed": p.Motor.MaximumRpm = p.Motor.MinimumRpm; break;
            case "step": p.Motor.StepRpm = 0; break;
            case "inspect": p.Motor.InspectRpm = 9000; break;
            case "zeroPole": p.Motor.PolePairs = 0; break;
            case "slots": p.Motor.Slots = 513; break;
            case "NaN": p.Motor.MaximumHz = double.NaN; break;
            case "tooManySteps": p.Motor.StepRpm = 0.1; break;
            case "negative": p.Motor.MinimumRpm = -1; break;
        }
        Reject(() => ProjectValidator.ThrowIfInvalid(p));
    });
}
Check("合法参数上下限", () =>
{
    var p = new Project();
    p.Motor.PolePairs = 64;
    p.Motor.Slots = 512;
    p.Motor.CommutatorSegments = 512;
    p.Motor.MaximumRpm = 100000;
    p.Motor.MaximumHz = 1000000;
    p.Motor.StepRpm = 50;
    ProjectValidator.ThrowIfInvalid(p);
    Assert(FrequencyEngine.Speeds(p.Motor).Count == 2001);
});
Check("扫描非整步长终点纳入", () =>
{
    var motor = new MotorConfiguration { MaximumRpm = 1000, StepRpm = 300, InspectRpm = 0 };
    Assert(FrequencyEngine.Speeds(motor).SequenceEqual(new double[] { 0, 300, 600, 900, 1000 }));
});
Check("解析频段转速1650至1950", () =>
{
    var row = FrequencyEngine.Crossings(demo).Single(x => x.CandidateId == "electrical" && x.BandName == "60 Hz 附近关注带");
    Near(row.LowerRpm, 1650);
    Near(row.UpperRpm, 1950);
});
Check("单点频段边界包含", () =>
{
    var p = new Project();
    p.Bands.Add(new SensitiveBand { LowerHz = 0, UpperHz = 0, Basis = "测试" });
    Assert(FrequencyEngine.Crossings(p).All(x => x.LowerRpm == 0 && x.UpperRpm == 0));
});
Check("同频多候选均保留", () =>
{
    var matches = FrequencyEngine.MatchPeaks(demo, demo.Spectra[0]);
    Assert(matches.Count(x => x.ObservedHz == 60) >= 2);
    Assert(matches.Count(x => x.ObservedHz == 360) >= 2);
});
Check("比较统一1800转当前60快照90差负30", () =>
{
    var row = FrequencyEngine.Compare(demo, demo.Scenarios[0]).Single(x => x.Name == "电气基频");
    Near(row.CurrentHz!.Value, 60);
    Near(row.SnapshotHz!.Value, 90);
    Near(row.DifferenceHz!.Value, -30);
});
var metadata = new SpectrumMetadata("测试谱", 1800, "dB", "dB re 20 μPa", "台架 12V", DataOrigin.Manual, "test.csv");
Check("CSV有效与BOM", () => Assert(SpectrumCsv.Parse("\uFEFF" + SpectrumCsv.Template(), metadata).Success));
foreach (var tuple in new[]
{
    ("坏表头", "hz,amp\n0,2\n1,3"),
    ("重复频率", "frequency_hz,amplitude\n1,2\n1,3"),
    ("降序", "frequency_hz,amplitude\n2,2\n1,3"),
    ("坏数字", "frequency_hz,amplitude\n0,2\n1,no"),
    ("NaN", "frequency_hz,amplitude\n0,2\n1,NaN"),
    ("额外列", "frequency_hz,amplitude\n0,2,5\n1,3"),
    ("中间空行", "frequency_hz,amplitude\n0,2\n\n1,3"),
    ("负频率", "frequency_hz,amplitude\n-1,2\n1,3"),
    ("仅一行", "frequency_hz,amplitude\n1,3")
})
{
    Check("CSV整批拒绝 " + tuple.Item1, () =>
    {
        var parsed = SpectrumCsv.Parse(tuple.Item2, metadata);
        Assert(!parsed.Success && parsed.Spectrum is null && parsed.Issues.Count > 0);
    });
}
Check("CSV线性幅值拒负数", () => Assert(!SpectrumCsv.Parse("frequency_hz,amplitude\n0,-1\n1,2", metadata with { Unit = "Pa" }).Success));
Check("CSV负dB可接受", () => Assert(SpectrumCsv.Parse("frequency_hz,amplitude\n0,-1\n1,2", metadata).Success));
Check("元信息缺参考量拒绝", () => Assert(!SpectrumCsv.Parse(SpectrumCsv.Template(), metadata with { Reference = "" }).Success));
Check("项目序列化完整往返", () =>
{
    var text = ProjectCodec.Serialize(demo);
    Assert(ProjectCodec.Serialize(ProjectCodec.Parse(text)) == text);
});
Check("损坏JSON拒绝", () => Reject(() => ProjectCodec.Parse("{not json")));
Check("未知版本拒绝", () => Reject(() => ProjectCodec.Parse(ProjectCodec.Serialize(demo).Replace("\"formatVersion\": 1", "\"formatVersion\": 99"))));
Check("未知字段拒绝", () =>
{
    var node = JsonNode.Parse(ProjectCodec.Serialize(demo))!.AsObject();
    node["newProperty"] = 1;
    Reject(() => ProjectCodec.Parse(node.ToJsonString()));
});
Check("缺少motor字段不静默默认", () =>
{
    var node = JsonNode.Parse(ProjectCodec.Serialize(demo))!;
    node["motor"]!.AsObject().Remove("polePairs");
    Reject(() => ProjectCodec.Parse(node.ToJsonString()));
});
Check("重复标识与空对象拒绝", () =>
{
    var p = ProjectCodec.Clone(demo);
    p.Bands.Add(ProjectCodec.Clone(p.Bands[0]));
    Reject(() => ProjectValidator.ThrowIfInvalid(p));
    p.Bands = [null!];
    Reject(() => ProjectValidator.ThrowIfInvalid(p));
});
Check("CSV公式注入转义", () =>
{
    Assert(ExportService.CsvCell("=SUM(A1)").StartsWith("\"'="));
    Assert(ExportService.CsvCell("  =SUM(A1)").StartsWith("\"'  ="));
});
Check("HTML用户文本编码", () =>
{
    var p = ProjectCodec.Clone(demo);
    p.Name = "<script>alert(1)</script>";
    var html = ExportService.Html(p);
    Assert(!html.Contains("<script>") && html.Contains("&lt;script&gt;"));
});
Check("导出频率CSV来源版本单位哈希", () =>
{
    var csv = ExportService.FrequencyCsv(demo);
    Assert(csv.Contains("frequency_hz") && csv.Contains("模拟演示") && csv.Contains(Software.Version) && csv.Contains("input_sha256"));
});
Check("SVG含真实候选路径", () => Assert(new ChartGeometry(demo.Motor).Svg(demo).Contains("M 68 316 L")));
Check("扫查9组含二极对60Hz", () =>
{
    var rows = ParameterSweep.Calculate(demo, new SweepRequest());
    Assert(rows.Any(x => x.PolePairs == 2 && x.Candidate == "电气基频" && x.FrequencyHz == 60 && x.WithinTolerance));
});
Check("扫查重复值和超限拒绝", () =>
{
    Reject(() => ParameterSweep.ParseIntegers("2,2", "极对数", 1, 64));
    Reject(() => ParameterSweep.ParseIntegers("0,2", "极对数", 1, 64));
});
Check("反算60Hz电频对应1800转", () =>
{
    var row = ParameterSweep.Reverse(demo, 60, 3).Single(x => x.Candidate == "电气基频");
    Near(row.CenterRpm, 1800); Near(row.LowerRpm, 1710); Near(row.UpperRpm, 1890);
});
Check("输入复核表与JSON包含来源边界", () =>
{
    Assert(ProjectReview.Inspect(demo).Count >= 10);
    Assert(ProjectReview.Json(demo).Contains("模拟演示"));
});
Check("频谱网格统计2Hz与最大幅值范围", () =>
{
    var summary = SpectrumStatistics.Inspect(demo.Spectra[0], 100);
    Near(summary.MedianSpacingHz, 2);
    Assert(summary.UniformSpacing && summary.TotalPoints == 751 && summary.PointsInsideView == 51);
});
Check("扫查上下文哈希稳定且响应参数变化", () =>
{
    var hash = ParameterSweep.ContextHash(demo);
    Assert(hash == ParameterSweep.ContextHash(demo));
    var p = ProjectCodec.Clone(demo);
    p.Motor.PolePairs = 3;
    Assert(hash != ParameterSweep.ContextHash(p));
});
Directory.CreateDirectory("docs");
Directory.CreateDirectory("examples");
File.WriteAllText("examples/60Hz-模拟案例.motorproj", ProjectCodec.Serialize(demo));
File.WriteAllText("examples/1800rpm-模拟频谱.csv", SpectrumCsv.Export(demo.Spectra[0]));
File.WriteAllText("examples/频谱模板.csv", SpectrumCsv.Template());
var evidence = new { at = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), passed = checks.Count - failures.Count, failed = failures.Count, checks };
File.WriteAllText("docs/core-evidence.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"TOTAL {checks.Count}; PASS {checks.Count - failures.Count}; FAIL {failures.Count}");
return failures.Count == 0 ? 0 : 1;
