using TrimContact.Core.Analysis;
using TrimContact.Core.IO;
using TrimContact.Core.Operations;

namespace TrimContact.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public BatchLayoutRequest BatchRequest { get; } = new();
    public BatchPreview? BatchResult { get; private set; }
    public DifferenceReport? Difference { get; private set; }
    public string ReferenceText { get; private set; } = "";
    private Guid? batchVariantId;

    public void PreviewBatch() => Execute(() =>
    {
        BatchResult = null;
        if (VariantId is null)
            throw new InvalidOperationException("先选择目标方案");
        BatchResult = BatchLayout.Preview(Draft, VariantId.Value, BatchRequest);
        batchVariantId = VariantId;
        Message("批量修改已预览，尚未应用。请逐行核对新旧值。");
    });

    public void ApplyBatch() => Request("确认应用预览中的全部参数修改？可在应用后撤销。", () =>
    {
        if (BatchResult is null || batchVariantId != VariantId)
            throw new InvalidOperationException("目标方案已变化或尚无预览，请重新预览");
        Draft = BatchLayout.Apply(Draft, BatchRequest, BatchResult);
        ApplyDraft();
        BatchResult = null;
        Message("批量参数修改已应用；请重新计算当前全部方案。");
    });

    public void CompareReference(string text) => Execute(() =>
    {
        var reference = ProjectCodec.Parse(text);
        Difference = ProjectDifference.Compare(Draft, reference);
        ReferenceText = text;
        Message("已核对当前输入与参考项目，工作项目未改变；按对象ID匹配差异。");
    });

    public void RefreshDifference() => Execute(() =>
    {
        if (ReferenceText.Length == 0)
            throw new InvalidOperationException("先读取参考项目文件");
        Difference = ProjectDifference.Compare(Draft, ProjectCodec.Parse(ReferenceText));
        Message("输入差异已更新。");
    });

    public bool DifferenceCurrent => Difference is not null
        && Difference.CurrentHash == ProjectCodec.InputHash(Draft);
}
