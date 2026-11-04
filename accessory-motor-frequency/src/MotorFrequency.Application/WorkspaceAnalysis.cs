using System.Globalization;
using System.Text;
using System.Text.Json;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Application;

public sealed partial class WorkspaceViewModel
{
    private void AddOrder()
    {
        var id = Guid.NewGuid();
        var name = Order.Name.Trim();
        var order = MotorDraft.Number(Order.Order, "自定义机械阶次");
        var basis = Order.Basis.Trim();
        Mutate("添加自定义候选", name, p => p.CustomOrders.Add(new CustomOrder
        {
            Id = id,
            Name = name,
            Order = order,
            Basis = basis
        }));
        Order.Name = "";
        Order.Basis = "";
        SelectedOrder = current!.CustomOrders.First(x => x.Id == id);
    }

    private void RemoveOrder()
    {
        var item = SelectedOrder ?? throw new InvalidDataException("请先选择自定义候选。");
        if (!interaction.Confirm("删除自定义候选“" + item.Name + "”？快照中的副本仍保留。"))
        {
            return;
        }
        Mutate("删除自定义候选", item.Name, p =>
        {
            p.CustomOrders.RemoveAll(x => x.Id == item.Id);
            p.Motor.HiddenCandidates.Remove("custom:" + item.Id);
        });
    }

    private void ToggleCandidate()
    {
        var candidate = SelectedCandidate ?? throw new InvalidDataException("请先选择一个候选。");
        Mutate("切换候选参与状态", candidate.Name, p =>
        {
            if (!p.Motor.HiddenCandidates.Remove(candidate.Id))
            {
                p.Motor.HiddenCandidates.Add(candidate.Id);
            }
        });
    }

    private void AddBand()
    {
        var id = Guid.NewGuid();
        var name = Band.Name.Trim();
        var low = MotorDraft.Number(Band.Lower, "频带下限 Hz");
        var high = MotorDraft.Number(Band.Upper, "频带上限 Hz");
        Mutate("添加敏感频带", name, p => p.Bands.Add(new SensitiveBand
        {
            Id = id,
            Name = name,
            LowerHz = low,
            UpperHz = high,
            Basis = Band.Basis.Trim(),
            Origin = Band.Origin
        }));
        Band.Name = "";
        Band.Basis = "";
        SelectedBand = current!.Bands.First(x => x.Id == id);
    }

    private void ToggleBand()
    {
        var band = SelectedBand ?? throw new InvalidDataException("请先选择一个频带。");
        Mutate("切换频带参与状态", band.Name, p =>
        {
            var target = p.Bands.First(x => x.Id == band.Id);
            target.Enabled = !target.Enabled;
        });
    }

    private void RemoveBand()
    {
        var band = SelectedBand ?? throw new InvalidDataException("请先选择一个频带。");
        if (interaction.Confirm("删除敏感频带“" + band.Name + "”？"))
        {
            Mutate("删除敏感频带", band.Name, p => p.Bands.RemoveAll(x => x.Id == band.Id));
        }
    }

    private void InspectCrossing()
    {
        var crossing = SelectedCrossing ?? throw new InvalidDataException("请先选择一个交叠区间。");
        SetInspect((crossing.LowerRpm + crossing.UpperRpm) / 2);
        Section = "candidates";
    }

    private void SetInspect(double rpm)
    {
        if (HasAppliedDraftChanges)
        {
            throw new InvalidDataException("移动观察转速前请先应用或重置条件草稿。");
        }
        Mutate("移动观察转速", Labels.Number(rpm) + " r/min", p => p.Motor.InspectRpm = rpm);
        Motor.InspectRpm = Labels.Number(current!.Motor.InspectRpm);
    }

    private void ReadCsv()
    {
        var path = interaction.OpenSpectrumFile();
        if (path is null)
        {
            return;
        }
        var bytes = DocumentFile.ReadLimited(path, SpectrumCsv.MaximumBytes);
        var content = new UTF8Encoding(false, true).GetString(bytes);
        Import.Csv = content;
        Import.FileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(Import.Name))
        {
            Import.Name = Path.GetFileNameWithoutExtension(path);
        }
        Message = "CSV已读入草稿。填写转速、单位、参考量、工况与来源后执行预检。";
    }

    private string ImportFingerprint()
    {
        return ProjectCodec.Hash(JsonSerializer.Serialize(new { Import.Csv, Metadata = Import.Metadata() }, ProjectCodec.Options));
    }

    private void PreviewCsv()
    {
        preview = SpectrumCsv.Parse(Import.Csv, Import.Metadata());
        previewHash = preview.Success ? ImportFingerprint() : "";
        Message = PreviewStatement;
        IsError = !preview.Success;
    }

    private void CommitCsv()
    {
        if (preview is null || !preview.Success || previewHash != ImportFingerprint())
        {
            throw new InvalidDataException("请先对当前CSV与元信息执行预检，全部通过后再确认导入。");
        }
        var spectrum = preview.Spectrum!;
        Mutate("导入频谱", spectrum.FileName + "；原始频点内嵌保存", p => p.Spectra.Add(ProjectCodec.Clone(spectrum)));
        SelectedSpectrum = current!.Spectra.First(x => x.Id == spectrum.Id);
        Import.Csv = "";
        Import.Name = "";
        Import.Reference = "";
        Import.Condition = "";
        preview = null;
        previewHash = "";
    }

    private void RemoveSpectrum()
    {
        var spectrum = SelectedSpectrum ?? throw new InvalidDataException("请先选择频谱。");
        if (interaction.Confirm("删除频谱“" + spectrum.Name + "”及其内嵌原始点？"))
        {
            Mutate("删除频谱", spectrum.Name, p => p.Spectra.RemoveAll(x => x.Id == spectrum.Id));
            SelectedSpectrum = current!.Spectra.FirstOrDefault();
        }
    }

    private void ToggleSpectrum()
    {
        var spectrum = SelectedSpectrum ?? throw new InvalidDataException("请先选择频谱。");
        Mutate("切换频谱峰点叠加", spectrum.Name, p =>
        {
            var target = p.Spectra.First(x => x.Id == spectrum.Id);
            target.Visible = !target.Visible;
        });
    }

    private void Capture()
    {
        if (HasAppliedDraftChanges)
        {
            throw new InvalidDataException("记录快照前请先应用档案、电机与复核草稿。");
        }
        var id = Guid.NewGuid();
        var name = ScenarioName.Trim();
        Mutate("记录方案快照", name, p => p.Scenarios.Add(new Scenario
        {
            Id = id,
            Name = name,
            Motor = ProjectCodec.Clone(p.Motor),
            CustomOrders = ProjectCodec.Clone(p.CustomOrders),
            Demonstration = p.Demonstration
        }));
        ScenarioName = "";
        SelectedScenario = current!.Scenarios.First(x => x.Id == id);
    }

    private void RemoveScenario()
    {
        var scenario = SelectedScenario ?? throw new InvalidDataException("请先选择方案快照。");
        if (interaction.Confirm("删除方案快照“" + scenario.Name + "”？"))
        {
            Mutate("删除方案快照", scenario.Name, p => p.Scenarios.RemoveAll(x => x.Id == scenario.Id));
            SelectedScenario = current!.Scenarios.FirstOrDefault();
        }
    }

    private void CalculateSweep()
    {
        if (current is null)
        {
            return;
        }
        if (HasAppliedDraftChanges)
        {
            throw new InvalidDataException("参数研究前请先应用或重置条件草稿。");
        }
        var request = new SweepRequest
        {
            ReferenceRpm = MotorDraft.Number(Sweep.Rpm, "扫查参考转速"),
            ObservedHz = MotorDraft.Number(Sweep.Frequency, "观察频率"),
            ToleranceHz = MotorDraft.Number(Sweep.Tolerance, "反算容差")
        };
        if (current.Motor.Topology == MotorTopology.BrushlessPermanentMagnet)
        {
            request.PolePairs = ParameterSweep.ParseIntegers(Sweep.Poles, "极对数列表", 1, 64).ToList();
            request.Slots = ParameterSweep.ParseIntegers(Sweep.Slots, "槽数列表", 1, 512).ToList();
        }
        else
        {
            request.Segments = ParameterSweep.ParseIntegers(Sweep.Segments, "换向片列表", 2, 512).ToList();
        }
        var context = ProjectCodec.Clone(current);
        var calculated = ParameterSweep.Calculate(context, request);
        var reversed = ParameterSweep.Reverse(context, request.ObservedHz, request.ToleranceHz);
        sweepRows = calculated;
        reverseRows = reversed;
        sweepContext = context;
        sweepDraftFingerprint = SweepFingerprint();
        sweepRequest = request;
        Message = "参数扫查和转速反算已完成；结果未自动保存到项目，请单独导出。";
        IsError = false;
    }

    private string SweepFingerprint()
    {
        return ProjectCodec.Hash(JsonSerializer.Serialize(new
        {
            Sweep.Poles, Sweep.Slots, Sweep.Segments, Sweep.Rpm, Sweep.Frequency, Sweep.Tolerance
        }));
    }

    private string ReverseCsv()
    {
        var output = new StringBuilder("project,source,observed_hz,tolerance_hz,candidate,order,center_rpm,lower_rpm,upper_rpm,within_scan,basis\n");
        foreach (var row in reverseRows)
        {
            string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
            output.AppendLine(string.Join(',', ExportService.CsvCell(sweepContext!.Name),
                ExportService.CsvCell(sweepContext.Demonstration ? "模拟演示" : "参数反算"),
                N(sweepRequest!.ObservedHz), N(sweepRequest.ToleranceHz), ExportService.CsvCell(row.Candidate),
                N(row.Order), N(row.CenterRpm), N(row.LowerRpm), N(row.UpperRpm),
                row.WithinScan ? "true" : "false", ExportService.CsvCell(row.Basis)));
        }
        return output.ToString();
    }

    private void Export()
    {
        if (current is null || SelectedExport is null)
        {
            return;
        }
        if (HasDraftChanges)
        {
            throw new InvalidDataException("导出前请先应用条件并完成新增表单，或重置草稿；确保导出内容与当前输入一致。");
        }
        var option = SelectedExport;
        string content;
        switch (option.Key)
        {
            case "report": content = ExportService.Html(current); break;
            case "frequency": content = ExportService.FrequencyCsv(current); break;
            case "crossing": content = ExportService.CrossingCsv(current); break;
            case "evidence": content = ExportService.EvidenceCsv(current); break;
            case "review": content = ProjectReview.Json(current); break;
            case "svg": content = new ChartGeometry(current.Motor).Svg(current); break;
            case "comparison":
                content = ExportService.ComparisonCsv(current, SelectedScenario
                    ?? throw new InvalidDataException("请先在方案快照页选择比较基准。"));
                break;
            case "raw":
                content = SpectrumCsv.Export(SelectedSpectrum ?? throw new InvalidDataException("请先在频谱复核页选择频谱。"));
                break;
            case "sweep":
            case "reverse":
                if (sweepContext is null || sweepRequest is null)
                {
                    throw new InvalidDataException("请先执行参数研究。" );
                }
                if (ParameterSweep.ContextHash(current) != ParameterSweep.ContextHash(sweepContext)
                    || SweepFingerprint() != sweepDraftFingerprint)
                {
                    throw new InvalidDataException("项目条件已改变，请重新执行参数研究后导出。" );
                }
                content = option.Key == "sweep" ? ParameterSweep.Csv(sweepContext, sweepRows) : ReverseCsv();
                break;
            default: throw new InvalidDataException("未知导出类型。");
        }
        WriteExport(ExportService.SafeName(current.Name) + "-" + option.Label, option.Extension, option.Description, content);
    }

    private void WriteExport(string name, string extension, string description, string content)
    {
        var path = interaction.SaveFile(name, extension, description);
        if (path is null)
        {
            return;
        }
        if (File.Exists(path) && !interaction.Confirm("覆盖已有成果文件？\n" + path))
        {
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(extension == ".csv"));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        LastOutput = Path.GetFullPath(path);
        Message = "已写入成果文件：" + LastOutput;
        IsError = false;
    }
}
