using TrimContact.Core.IO;

namespace TrimContact.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public string CsvInput { get; set; } = "";
    public bool CsvReplace { get; set; }
    public LayoutImportPreview? CsvPreview { get; private set; }
    public string CsvPreviewText { get; private set; } = "尚未预检";
    private string previewCsvHash = "";
    public string CsvTemplateUrl { get; private set; } = "";

    public void PreviewCsv() => Execute(() =>
    {
        CsvPreview = null;
        if (Project.Archived)
            throw new InvalidOperationException("归档项目只读，请先复制项目");
        if (VariantId is null)
            throw new InvalidOperationException("先在布置页建立并选择目标方案");
        CsvPreview = LayoutCsv.Preview(Draft, VariantId.Value, CsvInput, CsvReplace);
        CsvPreviewText = string.Join("\n", CsvPreview.Messages);
        previewCsvHash = ProjectCodec.FileHash(CsvInput);
        Message("CSV预检成功；工作项目尚未修改，请核对预览后应用导入。");
    });

    public void ApplyCsv() => Request("应用已预检CSV？替换模式会移除当前方案原有点位。", () =>
    {
        if (CsvPreview is null)
            throw new InvalidOperationException("先执行CSV预检");
        if (VariantId != CsvPreview.VariantId)
            throw new InvalidOperationException("目标方案已变化，请重新预检");
        if (ProjectCodec.FileHash(CsvInput) != previewCsvHash)
            throw new InvalidOperationException("CSV文本已修改，请重新预检");
        var verified = LayoutCsv.Preview(Draft, CsvPreview.VariantId, CsvInput, CsvReplace);
        if (verified.Mode != CsvPreview.Mode || CsvPreview.BaseHash != verified.BaseHash)
            throw new InvalidOperationException("预检后条件发生变化，请重新预检");
        if (CsvPreviewText != string.Join("\n", verified.Messages))
            throw new InvalidOperationException("预检后CSV发生变化，请重新预检");
        Draft = LayoutCsv.Apply(Draft, verified);
        ApplyDraft();
        CsvPreview = null;
        Message("CSV已整批应用，点位生成新ID；请核查来源、计算并保存。");
    });

    public void DownloadCsvTemplate() => Execute(() =>
    {
        CsvTemplateUrl = downloads.Add(LayoutCsv.Template, "text/csv; charset=utf-8", "布置导入模板-模拟.csv");
        Message("布置CSV模板已生成，模板包含模拟参数。");
    });

    public void ExportLayoutCsv() => Execute(() =>
    {
        ApplyDraft();
        if (Variant is null)
            throw new InvalidOperationException("先选择一个方案");
        var text = LayoutCsv.Export(Variant);
        DownloadName = "当前布置-" + Variant.Id.ToString("N")[..8] + ".csv";
        DownloadUrl = downloads.Add(text, "text/csv; charset=utf-8", DownloadName);
        Message("布置交换CSV已生成；它只包含点位参数，不携带项目来源或试验标定链。");
    });

    public void LoadCsvText(string text)
    {
        CsvInput = text;
        CsvPreview = null;
        CsvPreviewText = "CSV内容已载入，尚未预检";
        Message("CSV已读取到预检区，没有自动应用。");
    }
}
