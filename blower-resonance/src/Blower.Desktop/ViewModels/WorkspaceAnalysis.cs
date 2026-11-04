using System.IO;
using Blower.Core.Analysis;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Core.Operations;
using Blower.Desktop.Services;

namespace Blower.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public string StudyBands { get; set; } = "5,20,40";
    public string StudyTolerances { get; set; } = "0,20,50";
    private string csvKind = "gears";
    private string csvText = "";
    private string previewText = "";
    public CsvPreview? Preview { get; private set; }
    public string[] CsvKinds { get; } = ["gears", "modes"];
    public string CsvKind
    {
        get => csvKind;
        set
        {
            if (Set(ref csvKind, value))
            {
                Preview = null;
                Raise(nameof(PreviewStatus));
                RefreshRows();
            }
        }
    }
    public string CsvText
    {
        get => csvText;
        set
        {
            if (Set(ref csvText, value))
            {
                Preview = null;
                Raise(nameof(PreviewStatus));
                RefreshRows();
            }
        }
    }
    public string PreviewStatus => Preview == null ? "尚未预检。gears 表头：name,rpm,fixed；modes 表头：name,frequency_hz,damping_ratio,source" :
        Preview.Valid ? $"预检有效：{Preview.Gears.Count} 挡位，{Preview.Modes.Count} 模态。应用时再次核对输入和项目。" :
        $"预检失败：{Preview.Problems.Count} 项问题；行号 0 表示完整项目约束。";

    private async Task WorkAsync(string title, Func<Project, object> work, Action<object> apply)
    {
        if (busy || !LeaveEditor())
        {
            return;
        }
        busy = true;
        Raise(nameof(Busy));
        Message(title + "进行中…");
        try
        {
            var snapshot = ProjectCodec.Clone(Project);
            var hash = ProjectCodec.InputHash(snapshot);
            var result = await Task.Run(() => work(snapshot));
            if (hash != ProjectCodec.InputHash(Project))
            {
                throw new InvalidOperationException("计算期间输入变化，结果已丢弃，请重新运行");
            }
            apply(result);
            RefreshRows();
            RefreshChart();
            Raise(nameof(ResultStatus));
            Raise(nameof(ScanStatus));
            Raise(nameof(ComparisonStatus));
            Message(title + "完成。" + Software.Boundary);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            Message(title + "失败：" + e.Message, true);
        }
        finally
        {
            busy = false;
            Raise(nameof(Busy));
            RefreshCommands();
        }
    }

    private Task CalculateAsync()
    {
        return WorkAsync("交叉与挡位接近计算", p => Engine.Run(p), x => Result = (AnalysisResult)x);
    }

    private Task ScanAsync()
    {
        var id = ActivePlan?.Id ?? throw new InvalidOperationException("先选择方案");
        return WorkAsync("约束扫描", p => SpeedScanner.Scan(p, id), x => Scan = (ScanResult)x);
    }

    private Task StudyAsync()
    {
        var id = ActivePlan?.Id ?? throw new InvalidOperationException("先选择方案");
        var widths = StudyBands;
        var tolerances = StudyTolerances;
        return WorkAsync("规则敏感性试算", p => RuleStudy.Run(p, id, widths, tolerances), x => Study = (RuleStudyResult)x);
    }

    private void AcceptScan()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var scan = Scan ?? throw new InvalidOperationException("先运行扫描");
        if (dialogs.Confirm("将扫描结果建立为独立候选方案？仍需核对风量、效率和实测响应。"))
        {
            Change("接纳扫描候选", p => ProjectCommands.ApplyScan(p, scan));
        }
    }

    private AnalysisResult CurrentResult()
    {
        if (!LeaveEditor())
        {
            throw new InvalidOperationException("请先处理属性草稿");
        }
        if (Result == null || Result.InputHash != ProjectCodec.InputHash(Project))
        {
            throw new InvalidOperationException("没有有效计算结果，请先执行计算");
        }
        return Result;
    }

    private void Compare()
    {
        var result = CurrentResult();
        var baseline = ActivePlan?.Id ?? throw new InvalidOperationException("请选择基准方案");
        var target = ComparePlan?.Id ?? throw new InvalidOperationException("请选择目标方案");
        Comparison = Comparisons.Compare(Project, result, baseline, target);
        RefreshRows();
        Raise(nameof(ComparisonStatus));
        Message(Comparison.EqualGearCount ? "比较完成，差值为目标减基准。" : "比较完成；挡位数量不同，缺失位置不计算差值。");
    }

    private void GenerateChecks()
    {
        var result = CurrentResult();
        var id = ActivePlan?.Id ?? throw new InvalidOperationException("请选择方案");
        Change("生成验证清单", p => ProjectCommands.GenerateChecks(p, result, id), false);
    }

    private void ReadCsv()
    {
        var path = dialogs.OpenCsv();
        if (path != null)
        {
            CsvText = DesktopFiles.ReadCsv(path);
            Message("CSV 已读取到预检区，尚未修改项目：" + path);
        }
    }

    private void PreviewCsv()
    {
        if (!LeaveEditor())
        {
            return;
        }
        if (CsvText.Length > 1024 * 1024)
        {
            throw new InvalidDataException("CSV 超过 1MiB 预检上限");
        }
        Preview = CsvExchange.Preview(CsvText, CsvKind, Project);
        previewText = CsvText;
        RefreshRows();
        Raise(nameof(PreviewStatus));
        Message(PreviewStatus, !Preview.Valid);
    }

    private void ApplyCsv()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var preview = Preview;
        if (preview == null || !preview.Valid || previewText != CsvText || preview.Kind != CsvKind
            || preview.InputHash != ProjectCodec.InputHash(Project))
        {
            throw new InvalidOperationException("预检缺失、无效或已过期，请重新预检");
        }
        var checkedAgain = CsvExchange.Preview(CsvText, CsvKind, Project);
        if (!checkedAgain.Valid)
        {
            throw new InvalidOperationException("重新预检失败，请修正数据");
        }
        Change("应用CSV", p =>
        {
            if (preview.Kind == "gears")
            {
                p.Plans.Add(new() { Name = "CSV方案 " + (p.Plans.Count + 1), Gears = checkedAgain.Gears.ToList() });
            }
            else
            {
                p.Modes.AddRange(checkedAgain.Modes);
            }
        });
        Preview = null;
        RefreshRows();
        Raise(nameof(PreviewStatus));
    }

    private void ExportTemplate()
    {
        Export("csv", CsvKind == "gears" ? Exports.GearTemplate(ActivePlan) : Exports.ModeTemplate(Project));
    }

    private void Export(string extension, string content)
    {
        var path = dialogs.ExportPath(Project.Name, extension);
        if (path == null)
        {
            return;
        }
        DesktopFiles.WriteAtomic(path, content, extension == "csv");
        Message("导出成功：" + path);
    }

    private void ExportResult(string extension, Func<Project, AnalysisResult, string> produce)
    {
        var result = CurrentResult();
        Export(extension, produce(Project, result));
    }

    private void ExportReport()
    {
        var result = CurrentResult();
        var id = ActivePlan?.Id ?? throw new InvalidOperationException("请选择方案");
        Export("html", Exports.Report(Project, result, id));
    }

    private void ExportSvg()
    {
        var result = CurrentResult();
        var chart = ChartModel.Create(Project, result, ActivePlan?.Id ?? "", ChartCeiling);
        Export("svg", chart.ToSvg(Project.Name, Labels.Source(Project.Origin)));
    }

    private void ExportStudy()
    {
        if (Study == null)
        {
            throw new InvalidOperationException("先运行规则敏感性试算");
        }
        Export("csv", RuleStudy.ToCsv(Project, Study));
    }

    private void ExportComparison()
    {
        Compare();
        var comparison = Comparison!;
        var rows = new List<string>
        {
            CsvExchange.Row("software", "version", "project", "source", "input_sha256", "baseline", "target",
                "position", "baseline_rpm", "target_rpm", "delta_rpm", "baseline_contacts", "target_contacts", "note")
        };
        foreach (var row in comparison.Rows)
        {
            rows.Add(CsvExchange.Row(Software.Name, Software.Version, Project.Name, Labels.Source(Project.Origin),
                ProjectCodec.InputHash(Project), comparison.Baseline, comparison.Target, row.Position.ToString(),
                row.BaselineRpm.HasValue ? CsvExchange.Number(row.BaselineRpm.Value) : "",
                row.TargetRpm.HasValue ? CsvExchange.Number(row.TargetRpm.Value) : "",
                row.DeltaRpm.HasValue ? CsvExchange.Number(row.DeltaRpm.Value) : "",
                row.BaselineContacts?.ToString(), row.TargetContacts?.ToString(), row.Note));
        }
        Export("csv", string.Join(Environment.NewLine, rows));
    }
}
