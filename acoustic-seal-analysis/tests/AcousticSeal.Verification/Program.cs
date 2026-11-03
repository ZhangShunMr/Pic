using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AcousticSeal.Core.Analysis;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.Core.Operations;

var suite = new VerificationSuite();
await suite.RunAsync();
if (args.Length == 2 && args[0] == "--emit-examples")
{
    var destination = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(destination);
    var project = Demonstration.Create();
    var analysis = new AnalysisEngine().Calculate(project);
    var exporter = new ResultExport();
    foreach (var item in new[]
    {
        exporter.Project(project), exporter.Report(project, analysis),
        exporter.Measurements(project), exporter.Results(project, analysis), exporter.AnalysisJson(project, analysis)
    })
    {
        await File.WriteAllBytesAsync(Path.Combine(destination, item.Filename), item.Bytes);
    }
}

if (suite.Failed != 0)
{
    Environment.ExitCode = 1;
}

internal sealed class VerificationSuite
{
    private int passed;
    public int Failed { get; private set; }
    private readonly AnalysisEngine engine = new();
    private readonly ProjectCodec codec = new();

    public async Task RunAsync()
    {
        Test("独立数值：70减67等于3", () => Equal(Pair(Demo(), 40).Bands[0].Difference, 3));
        Test("独立数值：60与70能量均值", () => Near(AcousticMath.EnergyMean([60, 70]), 67.403626895, 1e-9));
        Test("独立数值：两个60频带能量和", () => Near(AcousticMath.EnergySum([60, 60]), 63.010299957, 1e-9));
        Test("空输入不补零", () => True(AcousticMath.EnergyMean([]) is null && AcousticMath.EnergySum([]) is null));
        Test("零与负声级有效", () => { Equal(AcousticMath.EnergyMean([0]), 0); Equal(AcousticMath.EnergyMean([-100]), -100); });
        Test("非有限运算输入拒绝", () => Throws<ArgumentException>(() => AcousticMath.EnergyMean([double.NaN])));
        Test("两种均值不能混同", DifferentMeans);
        Test("n等于1不生成区间", () => True(AcousticMath.Paired([70], [67], true).Lower95 is null));
        Test("复用试验不生成区间", ReusedBaseline);
        Test("独立Student区间常数", () => Near(AcousticMath.Paired([2, 4], [0, 0], true).Lower95, 3 - 12.706204736, 1e-8));
        Test("组合不相加", () => { var p = Demo(); Equal(Pair(p, 40).Bands[0].Difference, 3); Equal(Pair(p, 42).Bands[0].Difference, 2); Equal(Pair(p, 43).Bands[0].Difference, 4.5); });
        Test("负差值保留", () => { var p = Demo(); Trial(p, 32).Bands[0].Level = 72; Equal(Pair(p, 40).Bands[0].Difference, -2); });
        Test("明确缺失不生成差值或合成", () => { var p = Demo(); Trial(p, 32).Bands[0].Level = null; var r = Pair(p, 40); True(r.Bands[0].Difference is null && r.TotalDifference is null); });
        Test("频带集合不同不合成", () => { var p = Demo(); Trial(p, 32).Bands.RemoveAt(0); True(Pair(p, 40).TotalDifference is null); });
        Test("频带未声明不合成", () => { var p = Demo(); p.Conditions[0].NonOverlappingBands = false; True(Pair(p, 40).BaselineTotal is null); });
        Test("缺车辆字段阻断", () => { var p = Demo(); p.Conditions[0].Vehicle = ""; True(Pair(p, 40).Status == ResultStatus.Blocked); });
        Test("工况容差边界与超限", Tolerance);
        Test("仪器不同阻断", () => DifferentCondition(condition => condition.Instrument = "OTHER"));
        Test("计权不同阻断", () => DifferentCondition(condition => condition.Weighting = "A"));
        Test("混合来源阻断", () => { var p = Demo(); Trial(p, 32).Source = SourceKind.Measured; True(Pair(p, 40).Status == ResultStatus.Blocked); });
        Test("可选采样率检查", () => { var p = Demo(); p.Settings.RequireSampleRateMatch = true; p.Conditions[0].SampleRate = null; True(Pair(p, 40).Status == ResultStatus.Blocked); });
        Test("可选同步检查", () => { var p = Demo(); p.Settings.RequireSynchronizationMatch = true; p.Conditions[0].Synchronization = ""; True(Pair(p, 40).Status == ResultStatus.Blocked); });
        Test("缺失与未声明覆盖分列", Coverage);
        Test("恢复超限给出警示", () => { var p = Demo(); Trial(p, 37).Bands[0].Level = 73; True(Pair(p, 45).Screening?.Status == ResultStatus.Alert); });
        Test("单个重复样本无SD", () => { var p = Demo(); Trial(p, 34).RepeatGroup = "one"; var r = engine.Calculate(p).Repeatability.Single(g => g.Name == "one"); True(r.Bands[0].Statistics.StandardDeviation is null); });
        Test("损坏JSON拒绝", () => Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes("{bad"))));
        Test("重复JSON字段拒绝", () => Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"schemaVersion\":2}"))));
        Test("布尔格式版本拒绝", () => Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":true}"))));
        Test("未知格式版本拒绝", () => Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes("{\"schemaVersion\":99}"))));
        Test("未知字段拒绝", () => MutateJson("unknown", JsonValue.Create(1)));
        Test("缺必要集合拒绝", () => { var n = JsonNode.Parse(codec.Encode(Demo()))!.AsObject(); n.Remove("trials"); Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes(n.ToJsonString()))); });
        Test("缺试验来源字段拒绝而非猜测", () => MissingNested("source"));
        Test("缺试验标识拒绝而非新建", () => MissingNested("id"));
        Test("无时区日期拒绝", () => MutateJson("createdAt", JsonValue.Create("2026-01-01T10:00:00")));
        Test("声级边界与非有限校验", BadLevels);
        Test("位置重复与无效引用拒绝", InvalidReferences);
        Test("单项组合位置数量校验", () => { var p = Demo(); Trial(p, 35).LocationIds.RemoveAt(0); Throws<DomainValidationException>(() => engine.Calculate(p)); });
        Test("CSV空值和零值保留", CsvMissing);
        Test("CSV错误事务不改变项目", CsvInvalidTransaction);
        Test("CSV重复频率与未知试验", CsvDuplicates);
        Test("CSV合并和替换不同", CsvModes);
        Test("CSV过期预检拒绝", CsvStale);
        Test("CSV引号多行与分号", CsvQuotes);
        Test("UTF8损坏拒绝", () => Throws<DomainValidationException>(() => CsvImportService.DecodeUtf8([0xff, 0xfe])));
        Test("引用删除保护", ReferenceDelete);
        Test("撤销重做和单调修订", UndoRedo);
        Test("失败命令不改变历史", FailedCommand);
        Test("归档只读和解除", Archive);
        Test("复制试验不复制测量配对", DuplicateTrial);
        Test("项目副本独立标识", () => { var s = Session(); var id = s.Project.Id; s.MakeCopy(); True(s.Project.Id != id && s.Dirty); });
        Test("复核保存参数哈希与阻断原因", Review);
        Test("旧版v1迁移保留来源与数值", Migration);
        Test("项目保存编码重开相同", () => { var p = Demo(); var r = codec.Decode(codec.Encode(p)); True(!r.Migrated && r.Project.Trials.Count == 8); Equal(Pair(r.Project, 40).Bands[0].Difference, 3); });
        Test("报告转义并保留边界", ExportReport);
        Test("CSV包含版本参数及原因", ExportCsv);
        Test("分析JSON可检查", () => { var p = Demo(); using var d = JsonDocument.Parse(new ResultExport().AnalysisJson(p, engine.Calculate(p)).Bytes); True(d.RootElement.GetProperty("report").GetProperty("pairs").GetArrayLength() == 6); });
        Test("批量配对重复拒绝", BatchDuplicate);
        Test("批量配对显式目标", BatchExplicit);
        Test("版本核对按GUID频率识别", RevisionDiff);
        Test("原始核对区分缺失与未声明", Workbook);
        Test("参数情景不改项目", Scenarios);
        Test("频谱图缺失不插值", Chart);
        await TestAsync("实际文件保存、备份和搬迁重开", SaveAndReopen);
        await TestAsync("外部修改冲突不覆盖", DiskConflict);
        await TestAsync("协作实例文件锁阻止并行写入", FileLock);
        Console.WriteLine($"RESULT: passed={passed}; failed={Failed}; .NET={Environment.Version}; UTC={DateTimeOffset.UtcNow:O}");
    }

    private AcousticProject Demo() => Demonstration.Create();
    private static AcousticTrial Trial(AcousticProject p, int id) => p.Trials.Single(t => t.Id == Demonstration.StableId(id));
    private PairResult Pair(AcousticProject p, int id) => engine.Calculate(p).Pairs.Single(r => r.PairId == Demonstration.StableId(id));
    private ProjectSession Session() { var s = new ProjectSession(); s.Open(Demo()); return s; }
    private static void True(bool value) { if (!value) throw new Exception("assertion failed"); }
    private static void Equal(double? actual, double expected) => Near(actual, expected, 1e-12);
    private static void Near(double? actual, double expected, double tolerance) { if (!actual.HasValue || Math.Abs(actual.Value - expected) > tolerance) throw new Exception($"expected={expected}; actual={actual}"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("expected exception " + typeof(T).Name); }

    private void DifferentMeans()
    {
        var p = Demo();
        foreach (var (id, value) in new[] { (30, 60), (31, 70), (32, 60), (33, 60) }) Trial(p, id).Bands = [new BandMeasurement { Frequency = 125, Level = value }];
        var r = engine.Calculate(p).Effects.First().Bands[0].Statistics;
        Equal(r.MeanDifference, 5);
        Near(r.GroupEnergyMeanDifference, 7.403626895, 1e-9);
    }

    private void ReusedBaseline()
    {
        var p = Demo(); p.Pairs[1].BaselineId = Demonstration.StableId(30);
        var r = engine.Calculate(p).Effects[0];
        True(!r.IndependentPairs && r.Bands[0].Statistics.Lower95 is null);
    }

    private void Tolerance()
    {
        var p = Demo(); var c = ProjectCodec.Clone(p.Conditions[0]); c.Id = Guid.NewGuid(); c.Speed = 0.5;
        p.Conditions.Add(c); Trial(p, 32).ConditionId = c.Id;
        True(Pair(p, 40).Status == ResultStatus.Valid);
        c.Speed = 0.5001; True(Pair(p, 40).Status == ResultStatus.Blocked);
    }

    private void DifferentCondition(Action<MeasurementCondition> edit)
    {
        var p = Demo(); var c = ProjectCodec.Clone(p.Conditions[0]); c.Id = Guid.NewGuid(); edit(c); p.Conditions.Add(c); Trial(p, 32).ConditionId = c.Id;
        True(Pair(p, 40).Status == ResultStatus.Blocked);
    }

    private void Coverage()
    {
        var p = Demo(); Trial(p, 30).Bands[0].Level = null; Trial(p, 31).Bands.RemoveAt(0);
        var row = engine.Calculate(p).Coverage.Frequencies[0]; True(row.Missing == 1 && row.Undeclared == 1);
    }

    private void MissingNested(string name)
    {
        var node = JsonNode.Parse(codec.Encode(Demo()))!.AsObject();
        node["trials"]![0]!.AsObject().Remove(name);
        Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    private void MutateJson(string key, JsonNode? value)
    {
        var node = JsonNode.Parse(codec.Encode(Demo()))!.AsObject(); node[key] = value;
        Throws<DomainValidationException>(() => codec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    private void BadLevels()
    {
        foreach (var level in new[] { -101.0, 201, double.NaN, double.PositiveInfinity })
        {
            var p = Demo(); Trial(p, 30).Bands[0].Level = level; Throws<DomainValidationException>(() => engine.Calculate(p));
        }
    }

    private void InvalidReferences()
    {
        var p = Demo(); Trial(p, 32).LocationIds = [Guid.NewGuid()]; Throws<DomainValidationException>(() => engine.Calculate(p));
        p = Demo(); p.Locations[1].Id = p.Locations[0].Id; Throws<DomainValidationException>(() => engine.Calculate(p));
    }

    private string Csv(string rows) => "trial_id,frequency_hz,level_db\n" + rows;
    private void CsvMissing()
    {
        var p = Demo(); var preview = new CsvImportService().Preview(p, Csv($"{Trial(p, 30).Id},125,\n{Trial(p, 30).Id},250,0"), ImportMode.Merge);
        True(preview.CanApply && preview.Missing == 1); Equal(preview.Changes[0].Bands[1].Level, 0);
    }

    private void CsvInvalidTransaction()
    {
        var s = Session(); var before = ProjectCodec.Fingerprint(s.Project);
        var preview = new CsvImportService().Preview(s.Project, Csv($"{Trial(s.Project, 30).Id},125,80\n{Trial(s.Project, 30).Id},250,NaN"), ImportMode.Merge);
        Throws<DomainValidationException>(() => new CsvImportService().Apply(s, preview));
        True(before == ProjectCodec.Fingerprint(s.Project) && preview.Problems[0].Path.Contains('3'));
    }

    private void CsvDuplicates()
    {
        var p = Demo(); var id = Trial(p, 30).Id;
        True(!new CsvImportService().Preview(p, Csv($"{id},125,70\n{id},125.0,67"), ImportMode.Merge).CanApply);
        True(!new CsvImportService().Preview(p, Csv($"{Guid.NewGuid()},125,70"), ImportMode.Merge).CanApply);
    }

    private void CsvModes()
    {
        foreach (var mode in Enum.GetValues<ImportMode>())
        {
            var s = Session(); var c = new CsvImportService(); var preview = c.Preview(s.Project, Csv($"{Trial(s.Project, 30).Id},1000,50"), mode); c.Apply(s, preview);
            True(Trial(s.Project, 30).Bands.Count == (mode == ImportMode.Merge ? 4 : 1));
        }
    }

    private void CsvStale()
    {
        var s = Session(); var c = new CsvImportService(); var preview = c.Preview(s.Project, Csv($"{Trial(s.Project, 30).Id},125,80"), ImportMode.Merge);
        s.Apply("修改", "project", p => p.Name = "changed"); Throws<DomainValidationException>(() => c.Apply(s, preview));
    }

    private void CsvQuotes()
    {
        True(CsvParser.Read("a,b\n\"x\ny\",\"a\"\"b\"", ',')[1].Cells[0] == "x\ny");
        var p = Demo(); True(new CsvImportService().Preview(p, $"trial_id;frequency_hz;level_db\n{Trial(p, 30).Id};125;70", ImportMode.Merge, ';').CanApply);
        Throws<DomainValidationException>(() => CsvParser.Read("\"unclosed", ','));
    }

    private void ReferenceDelete()
    {
        var s = Session(); Throws<DomainValidationException>(() => ProjectCommands.DeleteLocation(s, Demonstration.StableId(10)));
        Throws<DomainValidationException>(() => ProjectCommands.DeleteCondition(s, Demonstration.StableId(20)));
        Throws<DomainValidationException>(() => ProjectCommands.DeleteTrial(s, Demonstration.StableId(30)));
    }

    private void UndoRedo()
    {
        var s = Session(); var original = s.Project.Name; s.Apply("修改名称", "project", p => p.Name = "new"); s.Undo();
        True(s.Project.Name == original && s.Project.Revision == 2); s.Redo(); True(s.Project.Name == "new" && s.Project.Revision == 3 && s.Project.Audit.Count == 3);
    }

    private void FailedCommand()
    {
        var s = Session(); Throws<DomainValidationException>(() => s.Apply("无效", "project", p => p.Name = "")); True(!s.CanUndo && s.Project.Revision == 0);
    }

    private void Archive()
    {
        var s = Session(); ProjectCommands.SetArchived(s, true); Throws<DomainValidationException>(() => s.Apply("不能编辑", "project", p => p.Name = "x")); ProjectCommands.SetArchived(s, false); True(s.Project.Status == ProjectStatus.Active);
    }

    private void DuplicateTrial()
    {
        var s = Session(); var id = ProjectCommands.DuplicateTrial(s, Demonstration.StableId(32)); var t = s.Project.Trials.Single(t => t.Id == id);
        True(t.Bands.Count == 0 && t.RepeatGroup == "" && s.Project.Pairs.Count == 6);
    }

    private void Review()
    {
        var s = Session(); s.Apply("缺字段", "condition", p => p.Conditions[0].Vehicle = ""); s.RecordReview("模拟复核人", "记录阻断");
        True(s.Project.Reviews[0].BlockedPairs == 6 && s.Project.Reviews[0].InputHash.Length == 64 && s.Project.Reviews[0].BlockingReasons.Count > 0);
    }

    private void Migration()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-v1.json")); var decoded = codec.Decode(bytes);
        True(decoded.Migrated && decoded.Project.SchemaVersion == 2 && decoded.Project.Trials.Count == 8);
        Equal(engine.Calculate(decoded.Project).Pairs[0].Bands[0].Difference, 3);
        True(decoded.Project.Trials.All(t => t.Source == SourceKind.Simulation));
    }

    private void ExportReport()
    {
        var p = Demo(); p.Name = "<script>alert(1)</script>"; p.Conditions[0].Vehicle = "";
        var text = Encoding.UTF8.GetString(new ResultExport().Report(p, engine.Calculate(p)).Bytes);
        True(!text.Contains("<script>") && text.Contains("车辆标识缺失") && text.Contains("路径独立能量贡献") && text.Contains("dB re 20"));
    }

    private void ExportCsv()
    {
        var p = Demo(); var text = Encoding.UTF8.GetString(new ResultExport().Results(p, engine.Calculate(p)).Bytes);
        True(text.Contains("software_version") && text.Contains("settings_json") && text.Contains("input_hash") && text.Contains("非实测"));
    }

    private void BatchDuplicate()
    {
        var s = Session(); var b = new BatchPairing(); var preview = b.Preview(s.Project, Demonstration.StableId(30), [Demonstration.StableId(32)], PairPurpose.Effect);
        True(!preview.CanApply); Throws<DomainValidationException>(() => b.Apply(s, preview));
    }

    private void BatchExplicit()
    {
        var s = Session(); s.Apply("移除配对", "pairs", p => p.Pairs = []); var b = new BatchPairing(); var preview = b.Preview(s.Project, Demonstration.StableId(30), [Demonstration.StableId(32), Demonstration.StableId(34)], PairPurpose.Effect); b.Apply(s, preview); True(s.Project.Pairs.Count == 2);
    }

    private void RevisionDiff()
    {
        var a = Demo(); var b = ProjectCodec.Clone(a); Trial(b, 32).Bands[0].Level = null;
        var diff = new RevisionComparison().Compare(a, b); True(diff.Changes.Any(c => c.Path.Contains("level") && c.After == "缺失(null)"));
    }

    private void Workbook()
    {
        var p = Demo(); Trial(p, 30).Bands[0].Level = null; Trial(p, 31).Bands.RemoveAt(0);
        var w = new InputWorkbook().Build(p, [Trial(p, 30).Id, Trial(p, 31).Id], null, null);
        True(w.Rows[0].Cells[0].Status == "明确缺失" && w.Rows[0].Cells[1].Status == "未声明");
    }

    private void Scenarios()
    {
        var p = Demo(); var original = codec.Encode(p); var result = new SettingsScenarios().Compare(p, nameof(AnalysisSettings.DriftThreshold), [0.2, 0.5, 1]);
        True(result.Scenarios[0].ScreeningAlerts == 1 && result.Scenarios[1].ScreeningAlerts == 0 && original.SequenceEqual(codec.Encode(p)));
    }

    private void Chart()
    {
        var p = Demo(); Trial(p, 32).Bands[1].Level = null; var svg = SpectrumChart.Build(Pair(p, 40));
        True(svg.Contains("缺失处断线") && svg.Contains("截断纵轴"));
    }

    private async Task SaveAndReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acoustic-verification-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(directory); var p = Demo(); var first = await store.SaveAsync(p, null);
            var loaded = await store.LoadAsync(p.Id); True(first.ETag == loaded.ETag); p.Name = "second"; var second = await store.SaveAsync(p, first.ETag);
            True(File.Exists(second.BackupPath)); var backup = await store.LoadBackupAsync(p.Id); True(backup.Project.Name != "second");
            var relocated = Path.Combine(directory, "relocated.sealproj"); File.Copy(second.Path, relocated); Equal(Pair(codec.Decode(await File.ReadAllBytesAsync(relocated)).Project, 40).Bands[0].Difference, 3);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private async Task DiskConflict()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acoustic-conflict-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(directory); var p = Demo(); var first = await store.SaveAsync(p, null); p.Name = "external"; await store.SaveAsync(p, first.ETag);
            p.Name = "stale"; try { await store.SaveAsync(p, first.ETag); throw new Exception("expected conflict"); } catch (ProjectConflictException) { }
            True((await store.LoadAsync(p.Id)).Project.Name == "external");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private async Task FileLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "acoustic-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(directory);
            var project = Demo();
            var file = store.PathFor(project.Id);
            using (var held = new FileStream(file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    await store.SaveAsync(project, null);
                    throw new Exception("expected sharing violation");
                }
                catch (IOException)
                {
                }
            }

            True(!File.Exists(file));
            await store.SaveAsync(project, null);
            True(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed++; Console.WriteLine("FAIL " + name + " :: " + error); }
    }

    private async Task TestAsync(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed++; Console.WriteLine("FAIL " + name + " :: " + error); }
    }
}
