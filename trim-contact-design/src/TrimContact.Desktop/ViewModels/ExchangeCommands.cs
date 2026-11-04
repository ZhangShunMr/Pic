using System.IO;
using TrimContact.Core.Analysis;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private void InitializeExchangeCommands()
    {
        CompareFileCommand = Command(() =>
        {
            ApplyDraft();
            var path = interaction.OpenPath("参考项目 (*.trimproj;*.bak)|*.trimproj;*.bak|所有文件|*.*");
            if (path is null) return;
            var reference = DesktopFiles.Open(path);
            difference = ProjectDifference.Compare(Applied, reference.Project);
            Raise(nameof(Differences));
            Raise(nameof(DifferenceSummary));
            Status = "已核对参考输入：" + path;
        }, () => !IsBusy && !HasConversionErrors);
        ReadCsvCommand = Command(() =>
        {
            var path = interaction.OpenPath("布置CSV (*.csv)|*.csv");
            if (path is null) return;
            ImportCsvText = DesktopFiles.ReadCsv(path);
            importPreview = null;
            Raise(nameof(ImportCsvText));
            Raise(nameof(ImportMessages));
            Status = "CSV已读入预检区，尚未修改设计。";
        }, () => CanEdit);
        CsvTemplateCommand = Command(() => ExportText("布置CSV (*.csv)|*.csv", "布置模板-模拟.csv",
            LayoutCsv.Template), () => !IsBusy);
        PreviewImportCommand = Command(() =>
        {
            var id = SelectedVariant?.Id ?? throw new InvalidDataException("先选择目标方案");
            ApplyDraft();
            importPreview = null;
            Raise(nameof(ImportMessages));
            var preview = LayoutCsv.Preview(Applied, id, ImportCsvText, ImportReplace);
            importedText = ImportCsvText;
            importedReplace = ImportReplace;
            importedVariant = id;
            importPreview = preview;
            Raise(nameof(ImportMessages));
            RefreshCommands();
            Status = "预检完成，请核对卡扣/接触数量和追加/替换模式，再确认应用。";
        }, () => CanEdit && !HasConversionErrors && SelectedVariant is not null);
        ApplyImportCommand = Command(() =>
        {
            RequireNoConversionErrors();
            if (DraftDirty || ImportCsvText != importedText || ImportReplace != importedReplace
                || SelectedVariant?.Id != importedVariant)
                throw new InvalidOperationException("预检后输入、目标方案或模式发生变化，请重新预检");
            if (importPreview is null) throw new InvalidOperationException("请先预检CSV");
            if (!interaction.Confirm("按预检结果" + importPreview.Mode + "点位？替换会解除旧点位的任务引用。")) return;
            var candidate = LayoutCsv.Apply(Applied, importPreview);
            session.Edit("应用CSV布置预检", p => ReplaceValues(p, candidate));
            ReloadDraft(importedVariant);
            Status = "CSV已事务应用，可用撤销恢复。请重新计算。";
        }, () => CanEdit && importPreview is not null && !HasConversionErrors);
        ExportLayoutCommand = Command(() =>
        {
            var id = SelectedVariant?.Id ?? throw new InvalidDataException("先选择目标方案");
            ApplyDraft();
            ExportText("布置CSV (*.csv)|*.csv", "当前方案布置.csv",
                LayoutCsv.Export(Applied.Variants.Single(v => v.Id == id)));
        }, () => !IsBusy && SelectedVariant is not null && !HasConversionErrors);
        PreviewBatchCommand = Command(() =>
        {
            var id = SelectedVariant?.Id ?? throw new InvalidDataException("先选择目标方案");
            ApplyDraft();
            batchPreview = null;
            Raise(nameof(BatchChanges));
            batchPreview = BatchLayout.Preview(Applied, id, BatchRequest);
            batchVariant = id;
            Raise(nameof(BatchChanges));
            RefreshCommands();
            Status = "批量预览完成，请核对每项修改后再应用。";
        }, () => CanEdit && SelectedVariant is not null && !HasConversionErrors);
        ApplyBatchCommand = Command(() =>
        {
            RequireNoConversionErrors();
            if (DraftDirty || SelectedVariant?.Id != batchVariant)
                throw new InvalidOperationException("预览后草稿或目标方案变化，请应用并重新预览");
            if (batchPreview is null) throw new InvalidOperationException("请先预览批量修改");
            var candidate = BatchLayout.Apply(Applied, BatchRequest, batchPreview);
            if (!interaction.Confirm("应用已预览的" + batchPreview.Changes.Count + "项批量修改？")) return;
            session.Edit("应用批量布置预览", p => ReplaceValues(p, candidate));
            ReloadDraft();
            Status = "批量修改已事务应用，可用撤销恢复。请重新计算。";
        }, () => CanEdit && batchPreview is not null && !HasConversionErrors);
    }
}
