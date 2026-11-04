using System.IO;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public string SweepDescription => SweepCurrent ? sweptTarget + " · " + sweptParameter
        + " · 基准SHA256 " + sweepHash : "尚无有效扫描。扫描仅修改计算副本，不自动应用参数。";

    private void InitializeAnalysisCommands()
    {
        CalculateCommand = BackgroundCommand(CalculateAsync, () => !IsBusy && !HasConversionErrors);
        SweepCommand = BackgroundCommand(SweepAsync,
            () => !IsBusy && !HasConversionErrors && SelectedContact is not null);
        CancelCommand = Command(() =>
        {
            cancellation?.Cancel();
            Status = "已请求取消，等待当前采样完成。";
        }, () => IsBusy);
        GenerateTasksCommand = Command(() =>
        {
            var added = 0;
            session.Edit("从有效结果生成验证任务", p => added = DesignOperations.GenerateTasks(p, report!));
            ReloadDraft();
            Status = $"新增{added}个验证任务，重复任务已跳过。完成状态只能由用户凭证据记录。";
            SectionIndex = 6;
        }, () => CanEdit && ResultsCurrent);
        ExportReportCommand = Command(() => ExportText("HTML报告 (*.html)|*.html", "接触设计报告.html",
            ResultExport.Html(Applied, report!)), () => !IsBusy && ResultsCurrent);
        ExportCsvCommand = Command(() => ExportText("CSV包络 (*.csv)|*.csv", "接触包络.csv",
            ResultExport.Csv(Applied, report!)), () => !IsBusy && ResultsCurrent);
        ExportTraceCommand = Command(() => ExportText("CSV名义相位 (*.csv)|*.csv", "名义相位.csv",
            ResultExport.TraceCsv(Applied, report!)), () => !IsBusy && ResultsCurrent);
        ExportJsonCommand = Command(() => ExportText("分析JSON (*.json)|*.json", "接触分析.json",
            ResultExport.Json(Applied, report!)), () => !IsBusy && ResultsCurrent);
        ExportBalanceCommand = Command(() => ExportText("CSV平衡核查 (*.csv)|*.csv", "平衡核查.csv",
            BalanceExport.Csv(Applied, report!)), () => !IsBusy && ResultsCurrent);
        ExportTasksCommand = Command(() =>
        {
            ApplyDraft();
            ExportText("CSV验证任务 (*.csv)|*.csv", "验证任务.csv", ResultExport.TasksCsv(Applied));
        }, () => !IsBusy && !HasConversionErrors);
        ExportSweepCommand = Command(() =>
        {
            var context = "\"扫描对象\",\"" + ("对象：" + (sweptTarget ?? "")).Replace("\"", "\"\"") + "\"\r\n";
            ExportText("CSV参数扫描 (*.csv)|*.csv", "参数扫描.csv",
                context + ResultExport.SweepCsv(Applied, sweptParameter!, sweepRows));
        }, () => !IsBusy && SweepCurrent);
    }

    private async Task CalculateAsync()
    {
        try
        {
            ApplyDraft();
            ProjectValidation.Require(Applied, true);
            var snapshot = ProjectCodec.Clone(Applied);
            IsBusy = true;
            Progress = 0;
            Status = "开始计算；输入已锁定。";
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var feedback = new Progress<(int Percent, string Text)>(value =>
            {
                Progress = value.Percent;
                Status = value.Text;
            });
            report = null;
            NotifyDraftEdited();
            var completed = await Task.Run(() => AnalysisEngine.Run(snapshot,
                (percent, message) => ((IProgress<(int, string)>)feedback).Report((percent, message)), token));
            report = completed;
            SectionIndex = 3;
            Status = $"已计算{completed.Variants.Count}个方案，来源：{completed.Origin}。结果是离散采样筛查，不预测真实声音。";
        }
        catch (OperationCanceledException)
        {
            report = null;
            Status = "计算已取消，没有保留部分结果。";
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException
            or UnauthorizedAccessException or ArgumentException)
        {
            report = null;
            Status = "计算未完成：" + error.Message;
            interaction.Error(Status);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            IsBusy = false;
            Raise(nameof(QualityRows));
            NotifyDraftEdited();
        }
    }

    private async Task SweepAsync()
    {
        try
        {
            var variantId = SelectedVariant?.Id ?? throw new InvalidDataException("先选择方案");
            var pointId = SelectedContact?.Id ?? throw new InvalidDataException("先选择接触点");
            var targetName = SelectedVariant!.Name + " / " + SelectedContact!.Name + " / " + pointId;
            var parameter = SweepParameter;
            var from = SweepFrom;
            var to = SweepTo;
            var steps = SweepSteps;
            ApplyDraft();
            ProjectValidation.Require(Applied, true);
            var snapshot = ProjectCodec.Clone(Applied);
            IsBusy = true;
            Progress = 0;
            Status = "扫描计算副本；结束后不会自动应用参数。";
            sweepRows = [];
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var completed = await Task.Run(() => AnalysisEngine.Sweep(snapshot, variantId, pointId,
                parameter, from, to, steps, token));
            sweepRows = completed;
            sweepHash = ProjectCodec.InputHash(snapshot);
            sweptParameter = parameter;
            sweptTarget = targetName;
            Progress = 100;
            Status = $"扫描完成：{targetName}，{parameter}，{from}至{to}，共{steps}值。未改变设计输入。";
        }
        catch (OperationCanceledException)
        {
            sweepRows = [];
            Status = "扫描已取消，没有保留部分结果。";
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException
            or UnauthorizedAccessException or ArgumentException)
        {
            sweepRows = [];
            Status = "扫描未完成：" + error.Message;
            interaction.Error(Status);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            IsBusy = false;
            Raise(nameof(SweepDescription));
            NotifyDraftEdited();
        }
    }

    private void ExportText(string filter, string suggested, string text)
    {
        var path = interaction.SavePath(filter, suggested);
        if (path is null) return;
        DesktopFiles.AtomicWrite(path, text);
        Status = "导出成功：" + Path.GetFullPath(path);
    }
}
