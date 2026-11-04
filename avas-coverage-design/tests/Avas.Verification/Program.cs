using System.Text;
using System.Text.Json.Nodes;
using Avas.Core.Domain;
using Avas.Core.Analysis;
using Avas.Core.IO;
using Avas.Core.Audio;

var checks = new List<object>();
var failures = new List<string>();
void Check(string name, Action action)
{
    try { action(); checks.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); checks.Add(new { name, status = "failed", error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool value, string message = "断言失败") { if (!value) throw new Exception(message); }
void Near(double a, double b, double tolerance = 1e-6) { Assert(Math.Abs(a - b) < tolerance, $"{a:R} != {b:R}"); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException) { return; } throw new Exception("应拒绝但成功了"); }
Project Fixture()
{
    var p = new Project { Name = "独立解析核对", Owner = "自动验证", Purpose = "解析公式核对" };
    p.Settings.SelectedHz = 1000;
    p.Speakers.Add(new Speaker { X = 0, Y = 0, Height = 1.5, Exponent = 0, Source = "人工核对：1m轴向60dB" });
    p.Listeners.Add(new Listener { Name = "2m核对点", X = 2, Y = 0 });
    return p;
}
var fixture = Fixture();
Check("单源2m几何扩散解析值", () => Near(FieldEngine.Calculate(fixture).Listeners[0].Signal, 60 - 20 * Math.Log10(2)));
Check("七带非计权合成", () => { var p = Fixture(); p.Settings.SelectedHz = 0; Near(FieldEngine.Calculate(p).Listeners[0].Signal, 60 - 20 * Math.Log10(2) + 10 * Math.Log10(7)); });
Check("双源非相干增加3.0103dB", () => { var p = Fixture(); var s = ProjectCodec.Clone(p.Speakers[0]); s.Id = Guid.NewGuid(); p.Speakers.Add(s); Near(FieldEngine.Calculate(p).Listeners[0].Signal, 60 - 20 * Math.Log10(2) + 10 * Math.Log10(2)); });
Check("高度差按三维距离", () => { var p = Fixture(); p.Speakers[0].Height = 0; Near(FieldEngine.Calculate(p).Listeners[0].Signal, 60 - 20 * Math.Log10(2.5)); });
Check("距离截断与标记", () => { var p = Fixture(); p.Listeners[0].X = 0; var q = FieldEngine.Calculate(p).Listeners[0]; Near(q.Signal, 60 - 20 * Math.Log10(0.5)); Assert(q.NearFieldClamped); });
Check("指向性轴向与背向", () => { var s = new Speaker { X = 0, Y = 0, Heading = 0, Exponent = 1, RearFloor = 0.2 }; Near(Acoustics.DirectionalLoss(s, 2, 0), 0); Near(Acoustics.DirectionalLoss(s, -2, 0), 20 * Math.Log10(.2)); });
Check("全向指数零", () => { var s = new Speaker { Exponent = 0 }; Near(Acoustics.DirectionalLoss(s, -50, 10), 0); });
Check("参考距离与频响增益叠加", () => { var p = Fixture(); p.Speakers[0].ReferenceDistance = 2; p.Speakers[0].Gain = 3; p.Speakers[0].Bands.Single(x => x.Hz == 1000).Correction = -2; Near(FieldEngine.Calculate(p).Listeners[0].Signal, 61); });
Check("背景与声源正确能量合成", () => { var r = FieldEngine.Calculate(fixture).Listeners[0]; Near(r.Margin, r.Signal - 35); Near(r.Combined, 10 * Math.Log10(Math.Pow(10, r.Signal / 10) + Math.Pow(10, 3.5))); });
Check("A计权只作用合成", () => { var p = Fixture(); p.Settings.AWeighted = true; Near(FieldEngine.Calculate(p).Listeners[0].Signal, FieldEngine.Calculate(fixture).Listeners[0].Signal); p.Settings.SelectedHz = 0; Near(FieldEngine.Calculate(p).Listeners[0].Signal, Acoustics.Sum(Software.AWeights.Select(x => x + 60 - 20 * Math.Log10(2)))); });
Check("零分贝是有效输入", () => { var p = Fixture(); p.Speakers[0].Bands.ForEach(x => x.Level = 0); Assert(Validation.Check(p, true).Count == 0); });
Check("空源计算拒绝", () => Reject(() => FieldEngine.Calculate(new Project())));
Check("缺频带拒绝", () => { var p = Fixture(); p.Speakers[0].Bands.RemoveAt(0); Reject(() => FieldEngine.Calculate(p)); });
Check("重复频率拒绝", () => { var p = Fixture(); p.Speakers[0].Bands[0].Hz = 250; Reject(() => FieldEngine.Calculate(p)); });
Check("非有限数拒绝", () => { var p = Fixture(); p.Speakers[0].X = double.NaN; Reject(() => FieldEngine.Calculate(p)); });
Check("负步长拒绝", () => { var p = Fixture(); p.Settings.Step = -1; Reject(() => FieldEngine.Calculate(p)); });
Check("网格上限拒绝", () => { var p = Fixture(); p.Settings.MinX = -100; p.Settings.MaxX = 100; p.Settings.MinY = -100; p.Settings.MaxY = 100; p.Settings.Step = .25; Reject(() => FieldEngine.Calculate(p)); });
Check("重复对象标识拒绝", () => { var p = Fixture(); p.Speakers.Add(ProjectCodec.Clone(p.Speakers[0])); Reject(() => FieldEngine.Calculate(p)); });
Check("工程目标两项同时满足", () => { var p = Fixture(); p.Settings.LevelTarget = 20; p.Settings.MarginTarget = 60; Assert(!FieldEngine.Calculate(p).Listeners[0].Covered); });
Check("车辆内部网格排除", () => { var r = FieldEngine.Calculate(fixture); Assert(r.Grid.All(x => !Acoustics.InsideVehicle(fixture.Settings, x.X, x.Y))); });
Check("全部节点被排除拒绝", () => { var p = Fixture(); p.Settings.MinX = -1; p.Settings.MaxX = 1; p.Settings.MinY = -.5; p.Settings.MaxY = .5; Reject(() => FieldEngine.Calculate(p)); });
Check("项目完整编码重开哈希一致", () => { var bytes = ProjectCodec.Encode(fixture); var p = ProjectCodec.Decode(bytes); Assert(ProjectCodec.InputHash(p) == ProjectCodec.InputHash(fixture)); });
Check("未知格式拒绝", () => { var node = JsonNode.Parse(ProjectCodec.Json(fixture))!; node["Schema"] = 2; Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()))); });
Check("损坏JSON拒绝", () => Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes("{bad}"))));
Check("未知字段拒绝", () => { var node = JsonNode.Parse(ProjectCodec.Json(fixture))!; node["Unknown"] = 1; Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()))); });
Check("CSV模板与零声级", () => { var text = SpectrumCsv.Template().Replace(",60,", ",0,"); var r = SpectrumCsv.Parse(text); Assert(r.Valid && r.Bands.All(x => x.Level == 0)); });
Check("CSV错误带行号并拒绝缺带", () => { var r = SpectrumCsv.Parse(SpectrumCsv.Header + "\n125,abc,0"); Assert(!r.Valid && r.Errors.Any(x => x.Contains("第2行"))); });
Check("背景CSV拒绝非零修正", () => Assert(!SpectrumCsv.Parse(SpectrumCsv.Template().Replace(",60,0", ",35,1"), true).Valid));
Check("CSV导出再导入", () => Assert(SpectrumCsv.Parse(Encoding.UTF8.GetString(SpectrumCsv.Write(fixture.Speakers[0].Bands))).Valid));
Check("点CSV引号转义与逗号", () => { var text = ListenerCsv.Header + "\n\"a,\"\"b\"\"\",2,3,test"; var r = ListenerCsv.Parse(text); Assert(r.Valid && r.Points[0].Name == "a,\"b\""); });
Check("点CSV非法坐标拒绝", () => Assert(!ListenerCsv.Parse(ListenerCsv.Header + "\np,NaN,0,n").Valid));
Check("点CSV交换重开", () => Assert(ListenerCsv.Parse(Encoding.UTF8.GetString(ListenerCsv.Write(fixture.Listeners))).Valid));
Check("方案快照深拷贝", () => { var p = Fixture(); var s = ScenarioEngine.Capture(p, "A"); p.Speakers[0].X = 40; Near(s.Speakers[0].X, 0); });
Check("相同条件不同增益可比", () => { var p = Fixture(); var a = ScenarioEngine.Capture(p, "A"); p.Speakers[0].Gain = 3; var b = ScenarioEngine.Capture(p, "B"); var r = ScenarioEngine.Compare(a, b); Assert(r.Comparable); Near(r.Points[0].Difference, 3); });
Check("不同背景阻断比较", () => { var p = Fixture(); var a = ScenarioEngine.Capture(p, "A"); p.Settings.Background[0].Level += 1; Assert(!ScenarioEngine.Compare(a, ScenarioEngine.Capture(p, "B")).Comparable); });
Check("重建同名点不能比较", () => { var p = Fixture(); var a = ScenarioEngine.Capture(p, "A"); p.Listeners[0].Id = Guid.NewGuid(); Assert(!ScenarioEngine.Compare(a, ScenarioEngine.Capture(p, "B")).Comparable); });
Check("扫描不修改输入", () => { var p = Fixture(); var hash = ProjectCodec.InputHash(p); var r = ScanEngine.Run(p, new ScanRequest { SpeakerId = p.Speakers[0].Id, Parameter = ScanParameter.Gain, Start = 0, End = 2, Step = 1 }); Assert(r.Rows.Count == 3 && ProjectCodec.InputHash(p) == hash); });
Check("扫描超范围拒绝", () => Reject(() => ScanEngine.Run(fixture, new ScanRequest { SpeakerId = fixture.Speakers[0].Id, Parameter = ScanParameter.Gain, Start = 0, End = 45, Step = 15 })));
Check("剖面解析值与计数", () => { var r = TransectEngine.Run(fixture, new TransectRequest { StartX = 2, Length = 2, Step = 1 }); Assert(r.Points.Count == 3); Near(r.Points[^1].Result.Signal, 60 - 20 * Math.Log10(4)); });
Check("剖面无效步长拒绝", () => Reject(() => TransectEngine.Run(fixture, new TransectRequest { Step = 0 })));
Check("过期结果禁止导出", () => { var p = Fixture(); var r = FieldEngine.Calculate(p); p.Speakers[0].Gain = 2; Reject(() => ResultExport.Csv(p, r, true)); });
Check("报告HTML转义且来源存在", () => { var p = Fixture(); p.Name = "<script>bad</script>"; var text = Encoding.UTF8.GetString(ResultExport.Report(p, FieldEngine.Calculate(p))); Assert(text.Contains("&lt;script&gt;") && !text.Contains("<script>bad") && text.Contains(p.Speakers[0].Source)); });
Check("CSV公式注入防护", () => Assert(SpectrumCsv.Cell("=2+2").Contains("'=2+2")));
Check("输出网格逐行计数", () => { var r = FieldEngine.Calculate(fixture); var text = Encoding.UTF8.GetString(ResultExport.Csv(fixture, r, true)); Assert(text.Trim().Split('\n').Length == r.Grid.Count + 1); });
var sine = Enumerable.Range(0, 24000).Select(i => .2 * Math.Sin(2 * Math.PI * 1000 * i / 24000)).ToArray();
var waveBytes = WaveCodec.Write(sine);
Check("WAV编码读取往返", () => { var w = WaveCodec.Read(waveBytes); Assert(w.Rate == 24000 && w.Channels == 1); Near(w.Duration, 1); Near(w.Samples[6], sine[6], 1.0 / 32768); });
Check("数字频谱1kHz主带", () => { var r = SpectralAudio.Analyze(WaveCodec.Read(waveBytes)); Assert(Array.IndexOf(r.Levels, r.Levels.Max()) == 3); Near(r.FullRms, .2 / Math.Sqrt(2), 1e-4); });
Check("FFT正逆变换", () => { var data = new System.Numerics.Complex[] { 1, 2, 3, 4 }; var old = data.ToArray(); Fourier.Transform(data); Fourier.Transform(data, true); for (var i = 0; i < 4; i++) Near(data[i].Real, old[i].Real); });
Check("静音不能推导物理频带", () => { var r = SpectralAudio.Analyze(WaveCodec.Read(WaveCodec.Write(new double[24000]))); Reject(() => SpectralAudio.RelativeBands(r, 70)); });
Check("非WAV拒绝", () => Reject(() => WaveCodec.Read(new byte[100])));
Check("WAV长度损坏拒绝", () => { var bad = waveBytes.ToArray(); bad[4] = 0; Reject(() => WaveCodec.Read(bad)); });
Check("内嵌音频搬迁重开", () => { var p = Fixture(); p.Speakers[0].Audio = WaveCodec.CreateAsset("tone.wav", waveBytes); var copy = ProjectCodec.Decode(ProjectCodec.Encode(p)); Assert(copy.Speakers[0].Audio!.Sha256 == ProjectCodec.Hash(waveBytes)); });
Check("音频哈希损坏拒绝", () => { var p = Fixture(); p.Speakers[0].Audio = WaveCodec.CreateAsset("tone.wav", waveBytes); p.Speakers[0].Audio!.Sha256 = "bad"; Reject(() => ProjectCodec.Decode(ProjectCodec.Encode(p))); });
Check("试听可编码且含边界说明", () => { var a = AuditionEngine.Render(fixture, fixture.Listeners[0], 1, 100, true); Assert(a.Peak < .98 && a.Description.Contains("未校准") && WaveCodec.Read(a.Wave).Samples.Length == 24000); });
Check("高峰值试听拒绝而非归一化", () => { var p = Fixture(); p.Speakers[0].Bands.ForEach(x => x.Level = 140); Reject(() => AuditionEngine.Render(p, p.Listeners[0], 1, 60, false)); });
Check("本机保存备份与外部冲突", () => { var root = Path.Combine(Path.GetTempPath(), "avas-test-" + Guid.NewGuid()); Directory.CreateDirectory(root); try { var store = new ProjectStore(root); var p = Fixture(); var first = store.Save(p, null); p.Note = "second"; var second = store.Save(p, first.Hash); Assert(second.BackupCreated); Assert(store.Open(p.Id, true).Project.Note == ""); p.Note = "third"; Reject(() => store.Save(p, first.Hash)); Assert(store.Open(p.Id).Project.Note == "second"); } finally { Directory.Delete(root, true); } });
Check("固定模拟例可计算且持续标记", () => { var p = Demonstration.Create(); var r = FieldEngine.Calculate(p); Assert(r.Warnings.Any(x => x.Contains("模拟")) && r.Listeners.Count == 4); });
Check("复核提示包含固定假设", () => Assert(ReviewEngine.Inspect(fixture).Any(x => x.Severity == "固定假设")));

Check("缺少结构字段不静默补默认", () => { var node = JsonNode.Parse(ProjectCodec.Json(fixture))!; node["Settings"]!.AsObject().Remove("Step"); Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()))); });
Check("数组空对象拒绝且不中断会话", () => { var node = JsonNode.Parse(ProjectCodec.Json(fixture))!; node["Speakers"]![0] = null; Reject(() => ProjectCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString()))); });
Check("快照哈希损坏拒绝", () => { var p = Fixture(); p.Snapshots.Add(ScenarioEngine.Capture(p, "A")); p.Snapshots[0].InputHash = "bad"; Reject(() => ProjectCodec.Decode(ProjectCodec.Encode(p))); });
Check("带制作元数据WAV可重读", () => { var bytes = WaveCodec.Write(sine, 24000, "相对试听 metadata"); var r = WaveCodec.Read(bytes); Near(r.Duration, 1); Assert(Encoding.UTF8.GetString(bytes).Contains("INFOICMT")); });
Check("扫描CSV附来源条件与模型", () => { var r = ScanEngine.Run(fixture, new ScanRequest { SpeakerId = fixture.Speakers[0].Id, Parameter = ScanParameter.Gain, Start = 0, End = 1, Step = 1 }); var csv = Encoding.UTF8.GetString(ResultExport.ScanCsv(r)); Assert(csv.Contains("Conditions") && csv.Contains("人工核对") && csv.Contains(Software.Model)); });
Check("模型条件摘要不内嵌原始背景音频", () => { var p = Fixture(); p.Settings.BackgroundAudio = WaveCodec.CreateAsset("tone.wav", waveBytes); var text = ProjectCodec.Conditions(p.Settings); Assert(text.Contains(p.Settings.BackgroundAudio.Sha256) && !text.Contains(p.Settings.BackgroundAudio.Data)); });

var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "docs", "core-verification.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, ProjectCodec.Json(new { date = DateTimeOffset.UtcNow, environment = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, total = checks.Count, passed = checks.Count - failures.Count, failed = failures.Count, checks }));
var artifacts = Path.Combine(Path.GetDirectoryName(output)!, "test-fixtures");
Directory.CreateDirectory(artifacts);
File.WriteAllBytes(Path.Combine(artifacts, "analytic-case.avasproj"), ProjectCodec.Encode(fixture));
File.WriteAllBytes(Path.Combine(artifacts, "digital-tone-1000Hz.wav"), waveBytes);
Console.WriteLine($"{checks.Count - failures.Count}/{checks.Count} passed. Evidence: {output}");
if (failures.Count > 0) Environment.ExitCode = 1;
